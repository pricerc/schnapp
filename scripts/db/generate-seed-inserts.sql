-- generate-seed-inserts.sql
--
-- Run this against the SOURCE database (e.g. psql -f generate-seed-inserts.sql -t -A > seed-data.sql)
-- It produces INSERT statements (in FK-safe order: customer -> project -> task)
-- that can then be executed against a NEW/empty database to populate it with
-- the same rows.
--
-- Usage:
--   psql "<source-connection-string>" -t -A -f generate-seed-inserts.sql > seed-data.sql
--   psql "<target-connection-string>" -f seed-data.sql
--
-- Notes:
--   * -t (tuples only) and -A (unaligned) strip psql's header/footer/formatting
--     so the output file is pure SQL.
--   * format(..., 'L') quote-literals each value (handles NULL, quotes, etc).
--   * Wrapped in a single transaction so a partial failure doesn't leave the
--     target database half-populated.

SELECT 'BEGIN;';

-- customer (no FK dependencies)
SELECT format(
  'INSERT INTO customer (id, code, name, notes, tags, status, colour_code, created_at, updated_at) VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L);',
  id, code, name, notes, tags, status, colour_code, created_at, updated_at
)
FROM customer
ORDER BY id;

-- project (depends on customer)
SELECT format(
  'INSERT INTO project (id, code, customer_id, name, notes, tags, status, colour_code, created_at, updated_at) VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L);',
  id, code, customer_id, name, notes, tags, status, colour_code, created_at, updated_at
)
FROM project
ORDER BY id;

-- task (depends on customer and project)
SELECT format(
  'INSERT INTO task (id, code, customer_id, project_id, name, notes, tags, status, colour_code, created_at, updated_at) VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L);',
  id, code, customer_id, project_id, name, notes, tags, status, colour_code, created_at, updated_at
)
FROM task
ORDER BY id;

SELECT 'COMMIT;';
