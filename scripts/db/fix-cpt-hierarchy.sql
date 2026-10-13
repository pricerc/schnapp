-- =============================================================================
-- Schnapp: audit + fix Customer / Project / Task / JournalTarget consistency
-- =============================================================================
-- Problem: client menu could apply mismatched C/P/T (e.g. project from customer A
-- with customer B selected). That lands as:
--   • task.customer_id ≠≠ project.customer_id
--   • journal_target.customer_id ≠≠ project.customer_id
--   • journal_target.project_id / task_id disagreeing with task's parents
--
-- Usage (read-only report first):
--   psql -d schnapp -v ON_ERROR_STOP=1 -f scripts/fix-cpt-hierarchy.sql
--
-- To APPLY fixes, re-run with:
--   psql -d schnapp -v ON_ERROR_STOP=1 -v apply=1 -f scripts/fix-cpt-hierarchy.sql
--
-- Default is REPORT ONLY (no writes). Wrap apply in a transaction you can ROLLBACK.
-- =============================================================================

\echo '=== Schnapp C/P/T hierarchy check ==='
\echo

\set NONE '''00000000-0000-0000-0000-000000000000'''

-- ---------------------------------------------------------------------------
-- 1) REPORT: tasks whose customer_id does not match their project's customer
-- ---------------------------------------------------------------------------
\echo '--- Tasks with customer_id ≠ project.customer_id ---'
SELECT
    t.id          AS task_id,
    t.code        AS task_code,
    t.name        AS task_name,
    t.customer_id AS task_customer_id,
    c_task.code   AS task_customer_code,
    t.project_id  AS project_id,
    p.code        AS project_code,
    p.customer_id AS project_customer_id,
    c_proj.code   AS project_customer_code
FROM task t
JOIN project p   ON p.id = t.project_id
JOIN customer c_task ON c_task.id = t.customer_id
JOIN customer c_proj ON c_proj.id = p.customer_id
WHERE t.customer_id <> p.customer_id
  AND t.id <> :NONE::uuid
ORDER BY t.code;

\echo
\echo '--- Journal targets: customer ≠ project.customer ---'
SELECT
    jt.id,
    jt.code,
    jt.name,
    jt.customer_id,
    c.code  AS customer_code,
    jt.project_id,
    p.code  AS project_code,
    p.customer_id AS project_customer_id,
    cp.code AS project_customer_code
FROM journal_target jt
JOIN project p  ON p.id = jt.project_id
JOIN customer c ON c.id = jt.customer_id
JOIN customer cp ON cp.id = p.customer_id
WHERE jt.customer_id <> p.customer_id
  AND jt.id <> :NONE::uuid
ORDER BY jt.code;

\echo
\echo '--- Journal targets: task parents disagree with target triple ---'
SELECT
    jt.id,
    jt.code,
    jt.customer_id  AS jt_customer,
    jt.project_id   AS jt_project,
    jt.task_id      AS jt_task,
    t.customer_id   AS task_customer,
    t.project_id    AS task_project,
    t.code          AS task_code
FROM journal_target jt
JOIN task t ON t.id = jt.task_id
WHERE jt.id <> :NONE::uuid
  AND (
        jt.customer_id <> t.customer_id
     OR jt.project_id  <> t.project_id
  )
ORDER BY jt.code;

\echo
\echo '--- Summary counts ---'
SELECT
    (SELECT count(*) FROM task t
     JOIN project p ON p.id = t.project_id
     WHERE t.customer_id <> p.customer_id AND t.id <> :NONE::uuid) AS bad_tasks,
    (SELECT count(*) FROM journal_target jt
     JOIN project p ON p.id = jt.project_id
     WHERE jt.customer_id <> p.customer_id AND jt.id <> :NONE::uuid) AS bad_jt_vs_project,
    (SELECT count(*) FROM journal_target jt
     JOIN task t ON t.id = jt.task_id
     WHERE jt.id <> :NONE::uuid
       AND (jt.customer_id <> t.customer_id OR jt.project_id <> t.project_id)) AS bad_jt_vs_task;

-- ---------------------------------------------------------------------------
-- 2) APPLY (only when -v apply=1)
-- Strategy (conservative):
--   A) task: set customer_id = project.customer_id
--      (project is the stronger parent for project-scoped tasks)
--   B) journal_target: align customer_id/project_id to the task's parents
--      (task defines the real work item). On unique-triple conflict, repoint
--      journal_entry / any refs to the surviving target, then delete the dup.
-- ---------------------------------------------------------------------------

\if :{?apply}
\else
\set apply 0
\endif

SELECT CASE WHEN :'apply' = '1'
    THEN '*** APPLY mode — will mutate data ***'
    ELSE '*** REPORT only — pass -v apply=1 to fix ***'
END AS mode;

\if :apply
BEGIN;

\echo
\echo '--- Applying task.customer_id := project.customer_id ---'
UPDATE task t
SET customer_id = p.customer_id
FROM project p
WHERE p.id = t.project_id
  AND t.customer_id <> p.customer_id
  AND t.id <> :NONE::uuid;

\echo 'tasks updated:' :ROW_COUNT

\echo
\echo '--- Align journal_target to task parents (where free) ---'
-- First pass: direct update when the corrected triple is not already taken
UPDATE journal_target jt
SET
    customer_id = t.customer_id,
    project_id  = t.project_id
FROM task t
WHERE t.id = jt.task_id
  AND jt.id <> :NONE::uuid
  AND (jt.customer_id <> t.customer_id OR jt.project_id <> t.project_id)
  AND NOT EXISTS (
        SELECT 1
        FROM journal_target other
        WHERE other.customer_id = t.customer_id
          AND other.project_id  = t.project_id
          AND other.task_id     = t.id
          AND other.id <> jt.id
  );

\echo 'journal_targets updated in place:' :ROW_COUNT

\echo
\echo '--- Merge journal_targets that would collide on the correct triple ---'
-- For remaining bad rows, an existing target already has (task.customer, task.project, task).
-- Repoint journal_entry → survivor, then delete the bad target.
WITH bad AS (
    SELECT
        jt.id AS bad_id,
        survivor.id AS good_id
    FROM journal_target jt
    JOIN task t ON t.id = jt.task_id
    JOIN journal_target survivor
      ON survivor.customer_id = t.customer_id
     AND survivor.project_id  = t.project_id
     AND survivor.task_id     = t.id
     AND survivor.id <> jt.id
    WHERE jt.id <> :NONE::uuid
      AND (jt.customer_id <> t.customer_id OR jt.project_id <> t.project_id)
),
repoint AS (
    UPDATE journal_entry je
    SET journal_target_id = bad.good_id,
        updated_at = now()
    FROM bad
    WHERE je.journal_target_id = bad.bad_id
    RETURNING je.id
)
SELECT count(*) AS journal_entries_repointed FROM repoint;

DELETE FROM journal_target jt
USING (
    SELECT jt2.id AS bad_id
    FROM journal_target jt2
    JOIN task t ON t.id = jt2.task_id
    JOIN journal_target survivor
      ON survivor.customer_id = t.customer_id
     AND survivor.project_id  = t.project_id
     AND survivor.task_id     = t.id
     AND survivor.id <> jt2.id
    WHERE jt2.id <> :NONE::uuid
      AND (jt2.customer_id <> t.customer_id OR jt2.project_id <> t.project_id)
) x
WHERE jt.id = x.bad_id;

\echo 'duplicate journal_targets deleted:' :ROW_COUNT

\echo
\echo '--- Refresh journal_target code/name from C/P/T when empty or stale composite ---'
UPDATE journal_target jt
SET
    code = COALESCE(NULLIF(trim(jt.code), ''), concat_ws('-',
        NULLIF(c.code, 'NONE'), NULLIF(p.code, 'NONE'), NULLIF(tk.code, 'NONE'))),
    name = COALESCE(NULLIF(trim(jt.name), ''), concat_ws(' / ',
        NULLIF(c.name, '(none)'), NULLIF(p.name, '(none)'), NULLIF(tk.name, '(none)')))
FROM customer c, project p, task tk
WHERE c.id = jt.customer_id
  AND p.id = jt.project_id
  AND tk.id = jt.task_id
  AND jt.id <> :NONE::uuid
  AND (jt.code = '' OR jt.name = '');

\echo
\echo '--- Post-fix summary (should be zeros) ---'
SELECT
    (SELECT count(*) FROM task t
     JOIN project p ON p.id = t.project_id
     WHERE t.customer_id <> p.customer_id AND t.id <> :NONE::uuid) AS bad_tasks,
    (SELECT count(*) FROM journal_target jt
     JOIN project p ON p.id = jt.project_id
     WHERE jt.customer_id <> p.customer_id AND jt.id <> :NONE::uuid) AS bad_jt_vs_project,
    (SELECT count(*) FROM journal_target jt
     JOIN task t ON t.id = jt.task_id
     WHERE jt.id <> :NONE::uuid
       AND (jt.customer_id <> t.customer_id OR jt.project_id <> t.project_id)) AS bad_jt_vs_task;

-- Review, then:
--   COMMIT;
-- or
--   ROLLBACK;
\echo
\echo 'Transaction left OPEN. Run COMMIT; or ROLLBACK; in this session.'

\else
\echo
\echo 'No changes applied. Review the report above, then:'
\echo '  psql -d schnapp -v ON_ERROR_STOP=1 -v apply=1 -f scripts/fix-cpt-hierarchy.sql'
\endif
