-- Status lookup + metadata columns on core entities

CREATE TABLE IF NOT EXISTS entity_status (
    entity      text NOT NULL,
    status      int  NOT NULL,
    description text NOT NULL,
    colour_code text NULL,
    PRIMARY KEY (entity, status)
);

INSERT INTO entity_status (entity, status, description, colour_code) VALUES
    ('Customer', 0, 'Active', NULL),
    ('Customer', 1, 'Inactive', NULL),
    ('Project', 0, 'Active', NULL),
    ('Project', 1, 'Inactive', NULL),
    ('Task', 0, 'Active', NULL),
    ('Task', 1, 'Inactive', NULL),
    ('User', 0, 'Active', NULL),
    ('User', 1, 'Inactive', NULL),
    ('Machine', 0, 'Active', NULL),
    ('Machine', 1, 'Disabled', NULL),
    ('UserMachine', 0, 'Active', NULL),
    ('UserMachine', 1, 'Inactive', NULL)
ON CONFLICT (entity, status) DO NOTHING;

ALTER TABLE customer
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS colour_code text NULL,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

ALTER TABLE project
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS colour_code text NULL,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

ALTER TABLE task
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS colour_code text NULL,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

ALTER TABLE app_user
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS colour_code text NULL,
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

ALTER TABLE machine
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS colour_code text NULL;

ALTER TABLE user_machine
    ADD COLUMN IF NOT EXISTS status int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now(),
    ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();

UPDATE user_machine
SET created_at = registered_at
WHERE created_at IS DISTINCT FROM registered_at
  AND registered_at IS NOT NULL;

UPDATE user_machine
SET updated_at = COALESCE(last_updated, registered_at, updated_at)
WHERE last_updated IS NOT NULL;
