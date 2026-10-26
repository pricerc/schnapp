-- Soft-delete. NULL means the screenshot is still in the gallery.
-- A timestamp means it is hidden until restored or permanently deleted.

ALTER TABLE screenshot
    ADD COLUMN deleted_at timestamptz NULL;

CREATE INDEX ix_screenshot_user_deleted_at
    ON screenshot (user_id, deleted_at);
