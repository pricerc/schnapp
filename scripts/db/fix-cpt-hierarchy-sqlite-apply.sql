-- Mutating section for client SQLite ref_* hierarchy fix.
-- Invoked by fix-cpt-hierarchy-sqlite.sh --apply (do not run alone without a backup).

BEGIN IMMEDIATE;

-- A) task.customer_id := project.customer_id
UPDATE ref_task
SET customer_id = (
    SELECT p.customer_id FROM ref_project p WHERE p.id = ref_task.project_id
)
WHERE project_id IS NOT NULL
  AND id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
  AND EXISTS (
        SELECT 1 FROM ref_project p
        WHERE p.id = ref_task.project_id
          AND p.customer_id IS NOT NULL
          AND ref_task.customer_id IS NOT NULL
          AND ref_task.customer_id <> p.customer_id
  );

SELECT changes() AS tasks_customer_fixed;

-- B) journal_target: align to task parents when the corrected triple is free
UPDATE ref_journal_target
SET
    customer_id = (SELECT t.customer_id FROM ref_task t WHERE t.id = ref_journal_target.task_id),
    project_id  = (SELECT t.project_id  FROM ref_task t WHERE t.id = ref_journal_target.task_id)
WHERE id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
  AND task_id IS NOT NULL
  AND EXISTS (
        SELECT 1 FROM ref_task t
        WHERE t.id = ref_journal_target.task_id
          AND t.customer_id IS NOT NULL
          AND t.project_id IS NOT NULL
          AND (ref_journal_target.customer_id <> t.customer_id
            OR ref_journal_target.project_id  <> t.project_id)
  )
  AND NOT EXISTS (
        SELECT 1 FROM ref_journal_target other
        JOIN ref_task t ON t.id = ref_journal_target.task_id
        WHERE other.customer_id = t.customer_id
          AND other.project_id  = t.project_id
          AND other.task_id     = t.id
          AND other.id <> ref_journal_target.id
  );

SELECT changes() AS journal_targets_updated_in_place;

-- C) delete bad journal_targets that would collide with an existing correct triple
--    (client has no journal_entry FK to repoint; cache-only)
DELETE FROM ref_journal_target
WHERE id IN (
    SELECT jt.id
    FROM ref_journal_target jt
    JOIN ref_task t ON t.id = jt.task_id
    JOIN ref_journal_target survivor
      ON survivor.customer_id = t.customer_id
     AND survivor.project_id  = t.project_id
     AND survivor.task_id     = t.id
     AND survivor.id <> jt.id
    WHERE jt.id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
      AND t.customer_id IS NOT NULL
      AND t.project_id IS NOT NULL
      AND (jt.customer_id <> t.customer_id OR jt.project_id <> t.project_id)
);

SELECT changes() AS journal_targets_deleted_as_dupes;

-- D) if still mismatched (no survivor), force-align to task anyway
UPDATE ref_journal_target
SET
    customer_id = (SELECT t.customer_id FROM ref_task t WHERE t.id = ref_journal_target.task_id),
    project_id  = (SELECT t.project_id  FROM ref_task t WHERE t.id = ref_journal_target.task_id)
WHERE id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
  AND EXISTS (
        SELECT 1 FROM ref_task t
        WHERE t.id = ref_journal_target.task_id
          AND t.customer_id IS NOT NULL
          AND t.project_id IS NOT NULL
          AND (ref_journal_target.customer_id <> t.customer_id
            OR ref_journal_target.project_id  <> t.project_id)
  );

SELECT changes() AS journal_targets_force_aligned;

-- E) fill empty code/name from C/P/T
UPDATE ref_journal_target
SET
    code = COALESCE(NULLIF(trim(code), ''), (
        SELECT trim(group_concat(x, '-'))
        FROM (
            SELECT c.code AS x FROM ref_customer c WHERE c.id = ref_journal_target.customer_id AND c.code <> 'NONE'
            UNION ALL
            SELECT p.code FROM ref_project p WHERE p.id = ref_journal_target.project_id AND p.code <> 'NONE'
            UNION ALL
            SELECT t.code FROM ref_task t WHERE t.id = ref_journal_target.task_id AND t.code <> 'NONE'
        )
    )),
    name = COALESCE(NULLIF(trim(name), ''), (
        SELECT trim(group_concat(x, ' / '))
        FROM (
            SELECT c.name AS x FROM ref_customer c WHERE c.id = ref_journal_target.customer_id AND c.name <> '(none)'
            UNION ALL
            SELECT p.name FROM ref_project p WHERE p.id = ref_journal_target.project_id AND p.name <> '(none)'
            UNION ALL
            SELECT t.name FROM ref_task t WHERE t.id = ref_journal_target.task_id AND t.name <> '(none)'
        )
    ))
WHERE id NOT IN ('00000000000000000000000002', '00000000-0000-0000-0000-000000000000')
  AND (code = '' OR name = '');

SELECT '--- Post-fix summary (want zeros) ---' AS section;

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

COMMIT;
SELECT 'Committed.' AS status;
