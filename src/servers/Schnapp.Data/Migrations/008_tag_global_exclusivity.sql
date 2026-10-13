-- A tag that exists as org-global must not also exist as customer-specific.
-- Drop customer-scoped duplicates where a global row already covers the same text.

DELETE FROM tag t
WHERE t.customer_id IS NOT NULL
  AND EXISTS (
      SELECT 1
      FROM tag g
      WHERE g.tag = t.tag
        AND g.customer_id IS NULL
  );
