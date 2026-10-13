-- Schnapp MVP initial schema

CREATE TABLE app_user (
    id              uuid PRIMARY KEY,
    code            text NOT NULL,
    name            text NOT NULL,
    email           text NOT NULL,
    password_hash   text NOT NULL,
    role            text NOT NULL,
    time_zone_id    text NOT NULL DEFAULT 'UTC',
    tags            text NULL,
    notes           text NULL,
    created_at      timestamptz NOT NULL,
    last_login      timestamptz NULL,
    last_seen       timestamptz NULL,
    CONSTRAINT uq_app_user_email UNIQUE (email),
    CONSTRAINT uq_app_user_code UNIQUE (code),
    CONSTRAINT uq_app_user_name UNIQUE (name)
);

CREATE TABLE api_token (
    id              uuid PRIMARY KEY,
    user_id         uuid NOT NULL REFERENCES app_user (id),
    token_hash      text NOT NULL,
    name            text NOT NULL,
    created_at      timestamptz NOT NULL,
    last_used_at    timestamptz NULL,
    revoked_at      timestamptz NULL,
    notes           text NULL,
    CONSTRAINT uq_api_token_hash UNIQUE (token_hash)
);

CREATE INDEX ix_api_token_user_id ON api_token (user_id);

CREATE TABLE machine (
    id                      uuid PRIMARY KEY,
    code                    text NOT NULL,
    name                    text NOT NULL,
    fqdn                    text NULL,
    hardware_fingerprint    text NULL,
    last_ip_addresses       jsonb NULL,
    optional_password_hash  text NULL,
    notes                   text NULL,
    tags                    text NULL,
    created_at              timestamptz NOT NULL,
    updated_at              timestamptz NOT NULL,
    last_seen               timestamptz NULL,
    CONSTRAINT uq_machine_code UNIQUE (code),
    CONSTRAINT uq_machine_name UNIQUE (name)
);

CREATE INDEX ix_machine_fqdn ON machine (fqdn);
CREATE INDEX ix_machine_hardware_fingerprint ON machine (hardware_fingerprint);

CREATE TABLE user_machine (
    user_id         uuid NOT NULL REFERENCES app_user (id),
    machine_id      uuid NOT NULL REFERENCES machine (id),
    registered_at   timestamptz NOT NULL,
    display_name    text NULL,
    notes           text NULL,
    tags            text NULL,
    last_seen       timestamptz NULL,
    last_updated    timestamptz NULL,
    client_settings jsonb NULL,
    PRIMARY KEY (user_id, machine_id)
);

CREATE TABLE setting (
    "key"         text PRIMARY KEY,
    value       text NOT NULL,
    notes       text NULL,
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE customer (
    id      uuid PRIMARY KEY,
    code    text NOT NULL,
    name    text NOT NULL,
    notes   text NULL,
    tags    text NULL,
    CONSTRAINT uq_customer_code UNIQUE (code),
    CONSTRAINT uq_customer_name UNIQUE (name)
);

CREATE TABLE project (
    id              uuid PRIMARY KEY,
    code            text NOT NULL,
    customer_id     uuid NOT NULL REFERENCES customer (id),
    name            text NOT NULL,
    notes           text NULL,
    tags            text NULL,
    CONSTRAINT uq_project_code UNIQUE (code),
    CONSTRAINT uq_project_name UNIQUE (name)
);

CREATE TABLE task (
    id              uuid PRIMARY KEY,
    code            text NOT NULL,
    customer_id     uuid NOT NULL REFERENCES customer (id),
    project_id      uuid NOT NULL REFERENCES project (id),
    name            text NOT NULL,
    notes           text NULL,
    tags            text NULL,
    CONSTRAINT uq_task_code UNIQUE (code),
    CONSTRAINT uq_task_name UNIQUE (name)
);

CREATE TABLE journal_target (
    id              uuid PRIMARY KEY,
    customer_id     uuid NOT NULL REFERENCES customer (id),
    project_id      uuid NOT NULL REFERENCES project (id),
    task_id         uuid NOT NULL REFERENCES task (id),
    code            text NOT NULL DEFAULT '',
    name            text NOT NULL DEFAULT '',
    notes           text NULL,
    tags            text NULL,
    CONSTRAINT uq_journal_target_triple UNIQUE (customer_id, project_id, task_id)
);

CREATE TABLE tag (
    tag     text PRIMARY KEY,
    notes   text NULL
);

CREATE TABLE journal_entry (
    id                  uuid PRIMARY KEY,
    user_id             uuid NOT NULL REFERENCES app_user (id),
    machine_id          uuid NOT NULL REFERENCES machine (id),
    start_utc           timestamptz NOT NULL,
    stop_utc            timestamptz NULL,
    journal_target_id   uuid NOT NULL REFERENCES journal_target (id),
    notes               text NULL,
    tags                text NULL,
    created_at          timestamptz NOT NULL,
    updated_at          timestamptz NOT NULL
);

CREATE INDEX ix_journal_entry_user_machine_start
    ON journal_entry (user_id, machine_id, start_utc);

CREATE UNIQUE INDEX ux_journal_one_open_per_user_machine
    ON journal_entry (user_id, machine_id)
    WHERE stop_utc IS NULL;

CREATE TABLE screenshot (
    id                      uuid PRIMARY KEY,
    user_id                 uuid NOT NULL REFERENCES app_user (id),
    machine_id              uuid NOT NULL REFERENCES machine (id),
    timestamp_utc           timestamptz NOT NULL,
    client_time_zone_id     text NULL,
    received_at_utc         timestamptz NOT NULL,
    monitor_id              text NULL,
    monitor_name            text NULL,
    window_title            text NULL,
    program_name            text NULL,
    command_line            text NULL,
    content_type            text NOT NULL,
    byte_size               bigint NOT NULL,
    width                   int NULL,
    height                  int NULL,
    sha256                  text NULL,
    storage_key             text NOT NULL,
    thumb_storage_key       text NOT NULL,
    notes                   text NULL,
    tags                    text NULL,
    active_journal_entry_id uuid NULL REFERENCES journal_entry (id),
    created_at              timestamptz NOT NULL
);

CREATE INDEX ix_screenshot_user_timestamp
    ON screenshot (user_id, timestamp_utc);

-- Well-known NONE sentinels (nil UUID)
INSERT INTO customer (id, code, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE', '(none)');

INSERT INTO project (id, code, customer_id, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE',
     '00000000-0000-0000-0000-000000000000', '(none)');

INSERT INTO task (id, code, customer_id, project_id, name) VALUES
    ('00000000-0000-0000-0000-000000000000', 'NONE',
     '00000000-0000-0000-0000-000000000000',
     '00000000-0000-0000-0000-000000000000', '(none)');

INSERT INTO journal_target (id, customer_id, project_id, task_id, code, name) VALUES
    ('00000000-0000-0000-0000-000000000000',
     '00000000-0000-0000-0000-000000000000',
     '00000000-0000-0000-0000-000000000000',
     '00000000-0000-0000-0000-000000000000',
     'NONE', '(none)');

INSERT INTO setting ("key", value, notes, updated_at) VALUES
    ('journal.recent_last_seen_minutes', '30',
     'Minutes: UserMachine.LastSeen is recent for journal auto-close', now());
