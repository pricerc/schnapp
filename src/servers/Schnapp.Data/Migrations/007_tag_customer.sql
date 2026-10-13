-- Optional customer-scoped tags + uniqueness for (tag, customer) pairs.
-- Global tags keep customer_id NULL. Same tag text may exist once globally
-- and once per customer.

ALTER TABLE tag
    ADD COLUMN IF NOT EXISTS customer_id uuid NULL REFERENCES customer (id);

ALTER TABLE tag DROP CONSTRAINT IF EXISTS tag_pkey;

-- Defensive: drop legacy single-column uniqueness if present as a named index
DROP INDEX IF EXISTS tag_pkey;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tag_global
    ON tag (tag)
    WHERE customer_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tag_customer
    ON tag (tag, customer_id)
    WHERE customer_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_tag_customer_id
    ON tag (customer_id);

-- Backfill dictionary entries from existing free-text tags columns.
-- Tokens are split on whitespace (API stores space-separated after normalize).

-- Global: user, machine, user_machine, screenshot
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text, NULL::uuid
FROM (
    SELECT unnest(regexp_split_to_array(btrim(tags), E'\\s+')) AS tok
    FROM app_user
    WHERE tags IS NOT NULL AND btrim(tags) <> ''
    UNION ALL
    SELECT unnest(regexp_split_to_array(btrim(tags), E'\\s+'))
    FROM machine
    WHERE tags IS NOT NULL AND btrim(tags) <> ''
    UNION ALL
    SELECT unnest(regexp_split_to_array(btrim(tags), E'\\s+'))
    FROM user_machine
    WHERE tags IS NOT NULL AND btrim(tags) <> ''
    UNION ALL
    SELECT unnest(regexp_split_to_array(btrim(tags), E'\\s+'))
    FROM screenshot
    WHERE tags IS NOT NULL AND btrim(tags) <> ''
) s
WHERE tok IS NOT NULL AND tok <> ''
ON CONFLICT DO NOTHING;

-- Customer entity tags → customer-scoped dictionary rows
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text, c.id
FROM customer c
CROSS JOIN LATERAL unnest(regexp_split_to_array(btrim(c.tags), E'\\s+')) AS tok
WHERE c.tags IS NOT NULL AND btrim(c.tags) <> ''
  AND tok IS NOT NULL AND tok <> ''
  AND c.id <> '00000000-0000-0000-0000-000000000000'::uuid
ON CONFLICT DO NOTHING;

-- Project tags → project.customer_id (skip NONE parent → global)
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text,
    CASE
        WHEN p.customer_id = '00000000-0000-0000-0000-000000000000'::uuid THEN NULL::uuid
        ELSE p.customer_id
    END
FROM project p
CROSS JOIN LATERAL unnest(regexp_split_to_array(btrim(p.tags), E'\\s+')) AS tok
WHERE p.tags IS NOT NULL AND btrim(p.tags) <> ''
  AND tok IS NOT NULL AND tok <> ''
ON CONFLICT DO NOTHING;

-- Task tags → task.customer_id (skip NONE parent → global)
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text,
    CASE
        WHEN t.customer_id = '00000000-0000-0000-0000-000000000000'::uuid THEN NULL::uuid
        ELSE t.customer_id
    END
FROM task t
CROSS JOIN LATERAL unnest(regexp_split_to_array(btrim(t.tags), E'\\s+')) AS tok
WHERE t.tags IS NOT NULL AND btrim(t.tags) <> ''
  AND tok IS NOT NULL AND tok <> ''
ON CONFLICT DO NOTHING;

-- Journal target tags → target.customer_id
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text,
    CASE
        WHEN jt.customer_id = '00000000-0000-0000-0000-000000000000'::uuid THEN NULL::uuid
        ELSE jt.customer_id
    END
FROM journal_target jt
CROSS JOIN LATERAL unnest(regexp_split_to_array(btrim(jt.tags), E'\\s+')) AS tok
WHERE jt.tags IS NOT NULL AND btrim(jt.tags) <> ''
  AND tok IS NOT NULL AND tok <> ''
ON CONFLICT DO NOTHING;

-- Journal entry tags → linked target.customer_id
INSERT INTO tag (tag, notes, customer_id)
SELECT DISTINCT tok, NULL::text,
    CASE
        WHEN jt.customer_id = '00000000-0000-0000-0000-000000000000'::uuid THEN NULL::uuid
        ELSE jt.customer_id
    END
FROM journal_entry je
JOIN journal_target jt ON jt.id = je.journal_target_id
CROSS JOIN LATERAL unnest(regexp_split_to_array(btrim(je.tags), E'\\s+')) AS tok
WHERE je.tags IS NOT NULL AND btrim(je.tags) <> ''
  AND tok IS NOT NULL AND tok <> ''
ON CONFLICT DO NOTHING;
