ALTER TABLE screenshot ADD deleted_at TIMESTAMP;

CREATE INDEX ix_screenshot_user_deleted_at
    ON screenshot (user_id, deleted_at);
