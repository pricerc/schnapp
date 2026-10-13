-- JPEG quality for stored screenshot originals (ingest re-encode with EXIF/XMP).

INSERT INTO setting (key, value, notes, updated_at) VALUES
    ('screenshot.jpeg_quality', '85',
     'JPEG quality for stored screenshot originals (1–100, default 85)', now())
ON CONFLICT (key) DO NOTHING;
