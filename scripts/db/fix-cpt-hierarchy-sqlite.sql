-- =============================================================================
-- Schnapp client SQLite: audit + fix ref_* Customer/Project/Task/JournalTarget
-- =============================================================================
-- Tables (in ~/.local/share/schnapp/schnapp.db, or legacy reference.db):
--   ref_customer, ref_project, ref_task, ref_journal_target
--
-- Ids are Id26 text. The NONE sentinel is usually:
--   00000000000000000000000002
-- (also accept all-zero UUID forms if present.)
--
-- Prefer the shell wrapper:
--   ./scripts/fix-cpt-hierarchy-sqlite.sh              # report
--   ./scripts/fix-cpt-hierarchy-sqlite.sh --apply      # fix + commit
--   ./scripts/fix-cpt-hierarchy-sqlite.sh /path/to.db
-- =============================================================================

.mode column
.headers on
.nullvalue ∅

SELECT '=== Client SQLite C/P/T hierarchy check ===' AS info;

-- ---------------------------------------------------------------------------
-- REPORT
-- ---------------------------------------------------------------------------
SELECT '--- Tasks: customer_id ≠ project.customer_id ---' AS section;

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
FROM ref_task t
JOIN ref_project p ON p.id = t.project_id
LEFT JOIN ref_customer c_task ON c_task.id = t.customer_id
LEFT JOIN ref_customer c_proj ON c_proj.id = p.customer_id
WHERE t.customer_id IS NOT NULL
  AND p.customer_id IS NOT NULL
  AND t.customer_id <> p.customer_id
  AND t.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
ORDER BY t.code;

SELECT '--- Journal targets: customer ≠ project.customer ---' AS section;

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
FROM ref_journal_target jt
JOIN ref_project p ON p.id = jt.project_id
LEFT JOIN ref_customer c ON c.id = jt.customer_id
LEFT JOIN ref_customer cp ON cp.id = p.customer_id
WHERE jt.customer_id <> p.customer_id
  AND jt.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
ORDER BY jt.code;

SELECT '--- Journal targets: disagree with task parents ---' AS section;

SELECT
    jt.id,
    jt.code,
    jt.customer_id AS jt_customer,
    jt.project_id  AS jt_project,
    jt.task_id     AS jt_task,
    t.customer_id  AS task_customer,
    t.project_id   AS task_project,
    t.code         AS task_code
FROM ref_journal_target jt
JOIN ref_task t ON t.id = jt.task_id
WHERE jt.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
  AND (
        (t.customer_id IS NOT NULL AND jt.customer_id <> t.customer_id)
     OR (t.project_id  IS NOT NULL AND jt.project_id  <> t.project_id)
  )
ORDER BY jt.code;

SELECT '--- Summary counts ---' AS section;

SELECT
    (SELECT count(*) FROM ref_task t
     JOIN ref_project p ON p.id = t.project_id
     WHERE t.customer_id IS NOT NULL AND p.customer_id IS NOT NULL
       AND t.customer_id <> p.customer_id
       AND t.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
    ) AS bad_tasks,
    (SELECT count(*) FROM ref_journal_target jt
     JOIN ref_project p ON p.id = jt.project_id
     WHERE jt.customer_id <> p.customer_id
       AND jt.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
    ) AS bad_jt_vs_project,
    (SELECT count(*) FROM ref_journal_target jt
     JOIN ref_task t ON t.id = jt.task_id
     WHERE jt.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
       AND (
            (t.customer_id IS NOT NULL AND jt.customer_id <> t.customer_id)
         OR (t.project_id  IS NOT NULL AND jt.project_id  <> t.project_id)
       )
    ) AS bad_jt_vs_task;

-- ---------------------------------------------------------------------------
-- APPLY (only when this file is run as the "apply" script via the shell wrapper,
-- which appends the apply section. Standalone: paste apply statements manually.)
-- See fix-cpt-hierarchy-sqlite-apply.sql for the mutating statements.
-- ---------------------------------------------------------------------------
