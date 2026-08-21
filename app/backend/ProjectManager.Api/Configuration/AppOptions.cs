namespace ProjectManager.Api.Configuration;

/// <summary>
/// Everything that differs between development, test, production and demo.
/// Produced only by <see cref="AppOptionsParser"/> - never constructed from
/// scattered configuration reads, so there is exactly one place where an
/// environment variable turns into behaviour.
/// </summary>
public sealed record AppOptions
{
    /// <summary>
    /// Demo mode serves generated fixture data instead of a personal database.
    /// This is the flag the public deployment runs with.
    /// </summary>
    public required bool DemoMode { get; init; }

    /// <summary>
    /// SQLite file path. Derived from <see cref="DemoMode"/> and, in demo mode,
    /// deliberately NOT overridable - see <see cref="AppOptionsParser"/>.
    /// </summary>
    public required string DatabasePath { get; init; }

    /// <summary>
    /// Explicit allow-list. Never contains "*": a wildcard origin combined with
    /// an unauthenticated write API is not something a configuration typo should
    /// be able to switch on.
    /// </summary>
    public required IReadOnlyList<string> AllowedCorsOrigins { get; init; }

    /// <summary>Swagger UI exposure. Off unless explicitly enabled.</summary>
    public required bool EnableSwagger { get; init; }

    /// <summary>Minimum log level for application logs.</summary>
    public required LogLevel MinimumLogLevel { get; init; }

    /// <summary>Short commit SHA the running build came from, or "unknown".</summary>
    public required string BuildCommit { get; init; }

    /// <summary>Build/version label, or "dev".</summary>
    public required string BuildVersion { get; init; }

    public string ConnectionString => $"Data Source={DatabasePath}";
}

/// <summary>
/// Result of parsing. Warnings are values, not exceptions: a typo in an
/// environment variable degrades to a documented default and is reported,
/// rather than crashing the process at startup.
/// </summary>
public sealed record AppOptionsResult(AppOptions Options, IReadOnlyList<string> Warnings);
