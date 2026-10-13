namespace Schnapp.Domain;

/// <summary>
/// Well-known keys in the org <c>setting</c> table, with documented defaults.
/// </summary>
/// <remarks>
/// Values are strings in the database; parse at the call site (or via
/// <c>SettingRepository</c> helpers). Admins may change values through
/// <c>PATCH /api/v1/settings/{key}</c> or the Web admin UI.
/// </remarks>
public static class SettingKeys
{
    /// <summary>
    /// Minutes after which <c>Machine.LastSeen</c> is treated as aged when auto-closing
    /// an open journal on a new start.
    /// </summary>
    /// <remarks>Default: <see cref="Defaults.JournalRecentLastSeenMinutes"/>.</remarks>
    public const string JournalRecentLastSeenMinutes = "journal.recent_last_seen_minutes";

    /// <summary>
    /// Maximum accepted screenshot upload size in bytes (multipart file part).
    /// </summary>
    /// <remarks>Default: <see cref="Defaults.ScreenshotMaxUploadBytes"/> (50 MiB).</remarks>
    public const string ScreenshotMaxUploadBytes = "screenshot.max_upload_bytes";

    /// <summary>
    /// Superseded by the fixed tiers in <c>ThumbTiers</c> (Schnapp.Contracts); no longer read.
    /// </summary>
    /// <remarks>Default: <see cref="Defaults.ScreenshotThumbMaxEdge"/> (640).</remarks>
    public const string ScreenshotThumbMaxEdge = "screenshot.thumb_max_edge";

    /// <summary>
    /// JPEG quality (1-100) for stored screenshot originals after ingest re-encode.
    /// </summary>
    /// <remarks>Default: <see cref="Defaults.ScreenshotJpegQuality"/> (85).</remarks>
    public const string ScreenshotJpegQuality = "screenshot.jpeg_quality";

    /// <summary>Hard-coded fallbacks when a setting row is missing or unparsable.</summary>
    public static class Defaults
    {
        /// <summary>Default for <see cref="JournalRecentLastSeenMinutes"/>.</summary>
        public const int JournalRecentLastSeenMinutes = 30;

        /// <summary>Default for <see cref="ScreenshotMaxUploadBytes"/> (50 MiB).</summary>
        public const long ScreenshotMaxUploadBytes = 50L * 1024 * 1024;

        /// <summary>Default for <see cref="ScreenshotThumbMaxEdge"/>.</summary>
        public const int ScreenshotThumbMaxEdge = 640;

        /// <summary>Default for <see cref="ScreenshotJpegQuality"/>.</summary>
        public const int ScreenshotJpegQuality = 85;
    }
}
