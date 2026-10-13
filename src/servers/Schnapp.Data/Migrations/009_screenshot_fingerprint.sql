-- Visual fingerprint (source-pixel grid + hashes) for idle/blank housekeeping.

ALTER TABLE screenshot
    ADD COLUMN fingerprint jsonb NULL;
