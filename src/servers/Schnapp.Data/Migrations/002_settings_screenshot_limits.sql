-- Configurable screenshot limits (alongside journal.recent_last_seen_minutes from 001_init)

INSERT INTO setting (key, value, notes, updated_at) VALUES
    ('screenshot.max_upload_bytes', '52428800',
     'Max multipart screenshot upload size in bytes (default 50 MiB)', now()),
    ('screenshot.thumb_max_edge', '640',
     'Max edge length in pixels for gallery thumbnails', now())
ON CONFLICT (key) DO NOTHING;
