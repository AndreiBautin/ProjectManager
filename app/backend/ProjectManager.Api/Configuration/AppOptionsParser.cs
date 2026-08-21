namespace ProjectManager.Api.Configuration;

/// <summary>
/// Turns raw environment values into <see cref="AppOptions"/>.
///
/// Two properties this type guarantees, both covered by tests:
///
/// 1. <b>Total.</b> It never throws and never returns null, for any input -
///    including null keys, empty strings and garbage. A mistyped environment
///    variable must not be able to take the process down at startup.
/// 2. <b>Never silently wrong.</b> Every value it could not use produces a
///    warning that the host logs. A typo degrades to a documented default
///    loudly, not quietly.
///
/// It is a pure function of its input dictionary - no ambient environment
/// reads, no clock, no file system - which is what makes it directly testable.
/// </summary>
public static class AppOptionsParser
{
    /// <summary>Database file used when demo mode is ON. Never holds real data.</summary>
    public const string DemoDatabasePath = "demo.db";

    /// <summary>Database file used when demo mode is OFF. The personal database.</summary>
    public const string PersonalDatabasePath = "projectmanager.db";

    private static readonly string[] DefaultDevOrigins =
    {
        "http://localhost:5174",
        "http://127.0.0.1:5174",
    };

    public static AppOptionsResult Parse(IReadOnlyDictionary<string, string?>? env)
    {
        env ??= new Dictionary<string, string?>();
        var warnings = new List<string>();

        var demoMode = ParseBool(env, "DEMO_MODE", defaultValue: false, warnings);

        var options = new AppOptions
        {
            DemoMode = demoMode,
            DatabasePath = ResolveDatabasePath(env, demoMode, warnings),
            AllowedCorsOrigins = ParseOrigins(env, warnings),
            EnableSwagger = ParseBool(env, "ENABLE_SWAGGER", defaultValue: false, warnings),
            MinimumLogLevel = ParseLogLevel(env, warnings),
            BuildCommit = ParseText(env, "BUILD_COMMIT", "unknown"),
            BuildVersion = ParseText(env, "BUILD_VERSION", "dev"),
        };

        return new AppOptionsResult(options, warnings);
    }

    /// <summary>
    /// Barrier #2 of the demo-data guarantee: the demo and personal datasets live
    /// in separate database files and <b>cannot be made to collide by
    /// configuration</b>. When demo mode is on, DATABASE_PATH is ignored
    /// outright rather than merely defaulted - so no environment typo, and no
    /// copy-pasted deploy config, can ever point demo seeding at the personal
    /// database. The override is honoured only when demo mode is off.
    /// </summary>
    private static string ResolveDatabasePath(
        IReadOnlyDictionary<string, string?> env, bool demoMode, List<string> warnings)
    {
        var raw = Get(env, "DATABASE_PATH");

        if (demoMode)
        {
            if (!string.IsNullOrWhiteSpace(raw) && raw.Trim() != DemoDatabasePath)
            {
                warnings.Add(
                    $"DATABASE_PATH='{raw.Trim()}' was ignored: demo mode always uses " +
                    $"'{DemoDatabasePath}' so demo seeding can never target a real database.");
            }
            return DemoDatabasePath;
        }

        return string.IsNullOrWhiteSpace(raw) ? PersonalDatabasePath : raw.Trim();
    }

    /// <summary>
    /// Comma-separated allow-list. A literal "*" is rejected with a warning:
    /// this API has no authentication, so wildcard CORS is never the intent,
    /// and a config value should not be able to enable it.
    /// </summary>
    private static IReadOnlyList<string> ParseOrigins(
        IReadOnlyDictionary<string, string?> env, List<string> warnings)
    {
        var raw = Get(env, "CORS_ALLOWED_ORIGINS");
        if (string.IsNullOrWhiteSpace(raw)) return DefaultDevOrigins;

        var accepted = new List<string>();
        foreach (var candidate in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (candidate == "*")
            {
                warnings.Add(
                    "CORS_ALLOWED_ORIGINS contained '*', which is not allowed on an " +
                    "unauthenticated API. The wildcard was dropped.");
                continue;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                warnings.Add($"CORS_ALLOWED_ORIGINS entry '{candidate}' is not an absolute http(s) URL and was dropped.");
                continue;
            }

            // Browsers compare origins scheme+host+port, with no trailing slash.
            var normalized = uri.GetLeftPart(UriPartial.Authority);
            if (!accepted.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                accepted.Add(normalized);
        }

        if (accepted.Count == 0)
        {
            warnings.Add("CORS_ALLOWED_ORIGINS produced no usable origins; falling back to local development origins.");
            return DefaultDevOrigins;
        }

        return accepted;
    }

    private static bool ParseBool(
        IReadOnlyDictionary<string, string?> env, string key, bool defaultValue, List<string> warnings)
    {
        var raw = Get(env, key);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes" or "on": return true;
            case "false" or "0" or "no" or "off": return false;
            default:
                warnings.Add($"{key}='{raw.Trim()}' is not a boolean; using default '{defaultValue}'.");
                return defaultValue;
        }
    }

    private static LogLevel ParseLogLevel(IReadOnlyDictionary<string, string?> env, List<string> warnings)
    {
        var raw = Get(env, "LOG_LEVEL");
        if (string.IsNullOrWhiteSpace(raw)) return LogLevel.Information;

        if (Enum.TryParse<LogLevel>(raw.Trim(), ignoreCase: true, out var parsed))
            return parsed;

        warnings.Add($"LOG_LEVEL='{raw.Trim()}' is not a valid level; using 'Information'.");
        return LogLevel.Information;
    }

    private static string ParseText(IReadOnlyDictionary<string, string?> env, string key, string fallback)
    {
        var raw = Get(env, key);
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private static string? Get(IReadOnlyDictionary<string, string?> env, string key)
        => env.TryGetValue(key, out var value) ? value : null;

    /// <summary>Convenience wrapper over the real process environment.</summary>
    public static AppOptionsResult FromEnvironment()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key != null) env[key] = entry.Value?.ToString();
        }
        return Parse(env);
    }
}
