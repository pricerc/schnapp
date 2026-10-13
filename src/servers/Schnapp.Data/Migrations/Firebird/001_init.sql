-- Schnapp catalog for Firebird 5 SuperServer (fresh install; equivalent to PG 001–010).
-- Instants are TIMESTAMP at UTC. Zones live on app_user.time_zone_id / screenshot.client_time_zone_id.
-- Ids are CHAR(16) OCTETS in RFC/network Uuid7 order.
-- dbup-firebird splits on a semicolon that is the only token on a line.

CREATE TABLE app_user (
    id              CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    code            VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    name            VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    email           VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    password_hash   VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    "role"          VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    time_zone_id    VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    tags            VARCHAR(1024) CHARACTER SET UTF8,
    notes           VARCHAR(2048) CHARACTER SET UTF8,
    status          INTEGER NOT NULL,
    colour_code     VARCHAR(255) CHARACTER SET UTF8,
    created_at      TIMESTAMP NOT NULL,
    updated_at      TIMESTAMP NOT NULL,
    last_login      TIMESTAMP,
    last_seen       TIMESTAMP,
    CONSTRAINT uq_app_user_email UNIQUE (email),
    CONSTRAINT uq_app_user_code UNIQUE (code),
    CONSTRAINT uq_app_user_name UNIQUE (name)
)
;

CREATE TABLE api_token (
    id              CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    user_id         CHAR(16) CHARACTER SET OCTETS NOT NULL,
    token_hash      VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    name            VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    created_at      TIMESTAMP NOT NULL,
    last_used_at    TIMESTAMP,
    revoked_at      TIMESTAMP,
    notes           VARCHAR(2048) CHARACTER SET UTF8,
    CONSTRAINT fk_api_token_user FOREIGN KEY (user_id) REFERENCES app_user (id),
    CONSTRAINT uq_api_token_hash UNIQUE (token_hash)
)
;

CREATE INDEX ix_api_token_user_id ON api_token (user_id)
;

CREATE TABLE machine (
    id                      CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    code                    VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    name                    VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    fqdn                    VARCHAR(255) CHARACTER SET UTF8,
    hardware_fingerprint    VARCHAR(255) CHARACTER SET UTF8,
    last_ip_addresses       BLOB SUB_TYPE TEXT CHARACTER SET UTF8,
    optional_password_hash  VARCHAR(255) CHARACTER SET UTF8,
    notes                   VARCHAR(2048) CHARACTER SET UTF8,
    tags                    VARCHAR(1024) CHARACTER SET UTF8,
    status                  INTEGER NOT NULL,
    colour_code             VARCHAR(255) CHARACTER SET UTF8,
    created_at              TIMESTAMP NOT NULL,
    updated_at              TIMESTAMP NOT NULL,
    last_seen               TIMESTAMP,
    CONSTRAINT uq_machine_code UNIQUE (code),
    CONSTRAINT uq_machine_name UNIQUE (name)
)
;

CREATE INDEX ix_machine_fqdn ON machine (fqdn)
;
CREATE INDEX ix_machine_hardware_fingerprint ON machine (hardware_fingerprint)
;

CREATE TABLE user_machine (
    user_id         CHAR(16) CHARACTER SET OCTETS NOT NULL,
    machine_id      CHAR(16) CHARACTER SET OCTETS NOT NULL,
    registered_at   TIMESTAMP NOT NULL,
    display_name    VARCHAR(255) CHARACTER SET UTF8,
    notes           VARCHAR(2048) CHARACTER SET UTF8,
    tags            VARCHAR(1024) CHARACTER SET UTF8,
    last_seen       TIMESTAMP,
    last_updated    TIMESTAMP,
    client_settings BLOB SUB_TYPE TEXT CHARACTER SET UTF8,
    status          INTEGER NOT NULL,
    created_at      TIMESTAMP NOT NULL,
    updated_at      TIMESTAMP NOT NULL,
    CONSTRAINT pk_user_machine PRIMARY KEY (user_id, machine_id),
    CONSTRAINT fk_user_machine_user FOREIGN KEY (user_id) REFERENCES app_user (id),
    CONSTRAINT fk_user_machine_machine FOREIGN KEY (machine_id) REFERENCES machine (id)
)
;

CREATE TABLE setting (
    "key"       VARCHAR(255) CHARACTER SET UTF8 NOT NULL PRIMARY KEY,
    "value"     VARCHAR(8191) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    updated_at  TIMESTAMP NOT NULL
)
;

CREATE TABLE customer (
    id          CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    code        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    name        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    tags        VARCHAR(1024) CHARACTER SET UTF8,
    status      INTEGER NOT NULL,
    colour_code VARCHAR(255) CHARACTER SET UTF8,
    created_at  TIMESTAMP NOT NULL,
    updated_at  TIMESTAMP NOT NULL,
    CONSTRAINT uq_customer_code UNIQUE (code),
    CONSTRAINT uq_customer_name UNIQUE (name)
)
;

CREATE TABLE project (
    id          CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    code        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    customer_id CHAR(16) CHARACTER SET OCTETS NOT NULL,
    name        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    tags        VARCHAR(1024) CHARACTER SET UTF8,
    status      INTEGER NOT NULL,
    colour_code VARCHAR(255) CHARACTER SET UTF8,
    created_at  TIMESTAMP NOT NULL,
    updated_at  TIMESTAMP NOT NULL,
    CONSTRAINT fk_project_customer FOREIGN KEY (customer_id) REFERENCES customer (id),
    CONSTRAINT uq_project_customer_code UNIQUE (customer_id, code),
    CONSTRAINT uq_project_name UNIQUE (name)
)
;

CREATE TABLE task (
    id          CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    code        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    customer_id CHAR(16) CHARACTER SET OCTETS NOT NULL,
    project_id  CHAR(16) CHARACTER SET OCTETS NOT NULL,
    name        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    tags        VARCHAR(1024) CHARACTER SET UTF8,
    status      INTEGER NOT NULL,
    colour_code VARCHAR(255) CHARACTER SET UTF8,
    created_at  TIMESTAMP NOT NULL,
    updated_at  TIMESTAMP NOT NULL,
    CONSTRAINT fk_task_customer FOREIGN KEY (customer_id) REFERENCES customer (id),
    CONSTRAINT fk_task_project FOREIGN KEY (project_id) REFERENCES project (id),
    CONSTRAINT uq_task_customer_project_code UNIQUE (customer_id, project_id, code),
    CONSTRAINT uq_task_name UNIQUE (name)
)
;

CREATE TABLE journal_target (
    id          CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    customer_id CHAR(16) CHARACTER SET OCTETS NOT NULL,
    project_id  CHAR(16) CHARACTER SET OCTETS NOT NULL,
    task_id     CHAR(16) CHARACTER SET OCTETS NOT NULL,
    code        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    name        VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    tags        VARCHAR(1024) CHARACTER SET UTF8,
    CONSTRAINT fk_journal_target_customer FOREIGN KEY (customer_id) REFERENCES customer (id),
    CONSTRAINT fk_journal_target_project FOREIGN KEY (project_id) REFERENCES project (id),
    CONSTRAINT fk_journal_target_task FOREIGN KEY (task_id) REFERENCES task (id),
    CONSTRAINT uq_journal_target_triple UNIQUE (customer_id, project_id, task_id)
)
;

CREATE TABLE tag (
    tag         VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes       VARCHAR(2048) CHARACTER SET UTF8,
    customer_id CHAR(16) CHARACTER SET OCTETS
)
;

CREATE UNIQUE INDEX ux_tag_global
    ON tag (tag)
    WHERE customer_id IS NULL
;

CREATE UNIQUE INDEX ux_tag_customer
    ON tag (tag, customer_id)
    WHERE customer_id IS NOT NULL
;

CREATE INDEX ix_tag_customer_id ON tag (customer_id)
;

CREATE TABLE journal_entry (
    id                  CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    user_id             CHAR(16) CHARACTER SET OCTETS NOT NULL,
    machine_id          CHAR(16) CHARACTER SET OCTETS NOT NULL,
    start_utc           TIMESTAMP NOT NULL,
    stop_utc            TIMESTAMP,
    journal_target_id   CHAR(16) CHARACTER SET OCTETS NOT NULL,
    notes               VARCHAR(2048) CHARACTER SET UTF8,
    tags                VARCHAR(1024) CHARACTER SET UTF8,
    created_at          TIMESTAMP NOT NULL,
    updated_at          TIMESTAMP NOT NULL,
    CONSTRAINT fk_journal_entry_user FOREIGN KEY (user_id) REFERENCES app_user (id),
    CONSTRAINT fk_journal_entry_machine FOREIGN KEY (machine_id) REFERENCES machine (id),
    CONSTRAINT fk_journal_entry_target FOREIGN KEY (journal_target_id) REFERENCES journal_target (id)
)
;

CREATE INDEX ix_journal_entry_user_machine_start
    ON journal_entry (user_id, machine_id, start_utc)
;

CREATE UNIQUE INDEX ux_journal_one_open_per_user_machine
    ON journal_entry (user_id, machine_id)
    WHERE stop_utc IS NULL
;

CREATE TABLE screenshot (
    id                      CHAR(16) CHARACTER SET OCTETS NOT NULL PRIMARY KEY,
    user_id                 CHAR(16) CHARACTER SET OCTETS NOT NULL,
    machine_id              CHAR(16) CHARACTER SET OCTETS NOT NULL,
    timestamp_utc           TIMESTAMP NOT NULL,
    client_time_zone_id     VARCHAR(255) CHARACTER SET UTF8,
    received_at_utc         TIMESTAMP NOT NULL,
    monitor_id              VARCHAR(255) CHARACTER SET UTF8,
    monitor_name            VARCHAR(255) CHARACTER SET UTF8,
    window_title            VARCHAR(2048) CHARACTER SET UTF8,
    program_name            VARCHAR(2048) CHARACTER SET UTF8,
    command_line            VARCHAR(2048) CHARACTER SET UTF8,
    content_type            VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    byte_size               BIGINT NOT NULL,
    width                   INTEGER,
    height                  INTEGER,
    sha256                  VARCHAR(255) CHARACTER SET UTF8,
    fingerprint             BLOB SUB_TYPE TEXT CHARACTER SET UTF8,
    storage_key             VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    thumb_storage_key       VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    notes                   VARCHAR(2048) CHARACTER SET UTF8,
    tags                    VARCHAR(1024) CHARACTER SET UTF8,
    journal_entry_id        CHAR(16) CHARACTER SET OCTETS,
    created_at              TIMESTAMP NOT NULL,
    CONSTRAINT fk_screenshot_user FOREIGN KEY (user_id) REFERENCES app_user (id),
    CONSTRAINT fk_screenshot_machine FOREIGN KEY (machine_id) REFERENCES machine (id),
    CONSTRAINT fk_screenshot_journal FOREIGN KEY (journal_entry_id) REFERENCES journal_entry (id)
)
;

CREATE INDEX ix_screenshot_user_timestamp
    ON screenshot (user_id, timestamp_utc)
;

CREATE TABLE entity_status (
    entity      VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    status      INTEGER NOT NULL,
    description VARCHAR(255) CHARACTER SET UTF8 NOT NULL,
    colour_code VARCHAR(255) CHARACTER SET UTF8,
    PRIMARY KEY (entity, status)
)
;

INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Customer', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Customer', 1, 'Inactive', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Project', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Project', 1, 'Inactive', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Task', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Task', 1, 'Inactive', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('User', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('User', 1, 'Inactive', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Machine', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('Machine', 1, 'Disabled', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('UserMachine', 0, 'Active', NULL)
;
INSERT INTO entity_status (entity, status, description, colour_code) VALUES ('UserMachine', 1, 'Inactive', NULL)
;

INSERT INTO customer (id, code, name, notes, tags, status, colour_code, created_at, updated_at)
VALUES (x'00000000000000000000000000000000', 'NONE', '(none)', NULL, NULL, 0, NULL,
        TIMESTAMP '2020-01-01 00:00:00', TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO project (id, code, customer_id, name, notes, tags, status, colour_code, created_at, updated_at)
VALUES (x'00000000000000000000000000000000', 'NONE', x'00000000000000000000000000000000', '(none)',
        NULL, NULL, 0, NULL, TIMESTAMP '2020-01-01 00:00:00', TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO task (id, code, customer_id, project_id, name, notes, tags, status, colour_code, created_at, updated_at)
VALUES (x'00000000000000000000000000000000', 'NONE', x'00000000000000000000000000000000',
        x'00000000000000000000000000000000', '(none)', NULL, NULL, 0, NULL,
        TIMESTAMP '2020-01-01 00:00:00', TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO journal_target (id, customer_id, project_id, task_id, code, name, notes, tags)
VALUES (x'00000000000000000000000000000000', x'00000000000000000000000000000000',
        x'00000000000000000000000000000000', x'00000000000000000000000000000000',
        'NONE', '(none)', NULL, NULL)
;

INSERT INTO setting ("key", "value", notes, updated_at)
VALUES ('journal.recent_last_seen_minutes', '30',
        'Minutes: UserMachine.LastSeen is recent for journal auto-close',
        TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO setting ("key", "value", notes, updated_at)
VALUES ('screenshot.max_upload_bytes', '52428800',
        'Max multipart screenshot upload size in bytes (default 50 MiB)',
        TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO setting ("key", "value", notes, updated_at)
VALUES ('screenshot.thumb_max_edge', '640',
        'Max edge length in pixels for gallery thumbnails',
        TIMESTAMP '2020-01-01 00:00:00')
;

INSERT INTO setting ("key", "value", notes, updated_at)
VALUES ('screenshot.jpeg_quality', '85',
        'JPEG quality for stored screenshot originals (1–100, default 85)',
        TIMESTAMP '2020-01-01 00:00:00')
;
