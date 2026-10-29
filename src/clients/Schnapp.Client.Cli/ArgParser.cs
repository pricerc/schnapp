namespace Schnapp.Cli;

using System;
using System.Collections.Generic;

/// <summary>
/// Hybrid argv parser: supports -k, --k, /k, and bare keyword keys from a whitelist.
/// </summary>
public sealed class ParsedArgs
{
    public List<string> Positionals { get; } = [];
    public Dictionary<string, string> Options { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Has(string key) => Flags.Contains(key) || Options.ContainsKey(key);

    public string? Get(string key, string? defaultValue = null)
    {
        return Options.TryGetValue(key, out string? v) ? v : defaultValue;
    }

    public bool GetFlag(string key) => Flags.Contains(key);
}

/// <summary>
/// Walks argv after the program name has been stripped.
/// </summary>
public static class ArgParser
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pass", "p",
        "at", "now",
        "notes", "note", "n",
        "tags", "tag", "t",
        "monitor", "mon",
        "machine", "m",
        "server", "api",
        "name", "code",
        "online-only", "onlineonly",
        "verbose", "v",
        "date",
        "from",
        "to",
        "limit",
        "all",
        "help", "h", "?"
    };

    /// <summary>
    /// Parses remaining tokens into positionals, options, and flags.
    /// </summary>
    /// <param name="args">Tokens after verb(s).</param>
    public static ParsedArgs Parse(string[] args)
    {
        var result = new ParsedArgs();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrEmpty(a))
            {
                continue;
            }

            string? inlineValue = null;


            string? key;
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                var body = a.Substring(2);
                SplitInline(body, out key, out inlineValue);
            }
            else if (a.StartsWith('-') && a.Length > 1 && !IsNumber(a))
            {
                var body = a.Substring(1);
                SplitInline(body, out key, out inlineValue);
            }
            else if (a.StartsWith('/') && a.Length > 1
                     && IsWindowsStyleSwitch(a))
            {
                // Windows /at 13:15 — do not treat Unix absolute paths (/home/...) as switches
                var body = a.Substring(1);
                SplitInline(body, out key, out inlineValue);
            }
            else if (KnownKeys.Contains(a))
            {
                key = NormalizeKey(a);
            }
            else
            {
                result.Positionals.Add(a);
                continue;
            }

            key = NormalizeKey(key);
            if (key == "help" || key == "h" || key == "?")
            {
                result.Flags.Add("help");
                continue;
            }

            if (key == "now")
            {
                result.Flags.Add("now");
                continue;
            }

            if (inlineValue != null)
            {
                result.Options[key] = inlineValue;
                continue;
            }

            if (i + 1 < args.Length && !LooksLikeKey(args[i + 1]))
            {
                result.Options[key] = args[++i];
            }
            else
            {
                result.Flags.Add(key);
            }
        }

        return result;
    }

    private static void SplitInline(string body, out string key, out string? value)
    {
        var eq = body.IndexOf('=');
        if (eq >= 0)
        {
            key = body.Substring(0, eq);
            value = body.Substring(eq + 1);
        }
        else
        {
            key = body;
            value = null;
        }
    }

    private static string NormalizeKey(string key)
    {
        if (key == null)
        {
            return "";
        }

        key = key.Trim().ToLowerInvariant();

        return key switch
        {
            "pass" or "p" => "password",
            "note" or "n" => "notes",
            "tag" or "t" => "tags",
            "mon" => "monitor",
            "m" => "machine",
            "api" => "server",
            _ => key,
        };
    }

    private static bool LooksLikeKey(string a)
    {
        if (string.IsNullOrEmpty(a))
        {
            return false;
        }

        if (a.StartsWith('-') || a.StartsWith('/'))
        {
            return true;
        }

        return KnownKeys.Contains(a);
    }

    private static bool IsNumber(string a)
    {
        if (a.Length < 2)
        {
            return false;
        }
        // allow negative numbers as positionals rarely; treat -1 as key-ish skip
        double _;
        return double.TryParse(a, out _);
    }

    /// <summary>
    /// True for Windows-style switches like /at or /at=1, false for paths like /home/x.png.
    /// </summary>
    private static bool IsWindowsStyleSwitch(string a)
    {
        if (!a.StartsWith('/') || a.Length < 2)
        {
            return false;
        }

        // Path exists on disk, treat as path, not switch
        if (System.IO.Path.Exists(a))
        {
            return false;
        }

        var body = a.Substring(1);
        SplitInline(body, out string key, out string? _);
        return KnownKeys.Contains(key);
    }
}
