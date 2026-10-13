-- Align machine status wording: 0 = Active, 1 = Disabled
UPDATE entity_status
SET description = 'Disabled'
WHERE entity = 'Machine' AND status = 1;

INSERT INTO entity_status (entity, status, description, colour_code) VALUES
    ('Machine', 0, 'Active', NULL),
    ('Machine', 1, 'Disabled', NULL)
ON CONFLICT (entity, status) DO UPDATE
SET description = EXCLUDED.description;
