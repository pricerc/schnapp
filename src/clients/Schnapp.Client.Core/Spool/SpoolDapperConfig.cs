namespace Schnapp.Client.Core.Spool;

using Dapper;

/// <summary>
/// One-time Dapper configuration for the client-side SQLite spool index.
/// </summary>
internal static class SpoolDapperConfig
{
    private static int _configured;

    /// <summary>Enables underscore-to-Pascal column mapping. Safe to call multiple times.</summary>
    public static void EnsureConfigured()
    {
        if (System.Threading.Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
}
