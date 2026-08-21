using Microsoft.Extensions.Logging;
using ProjectManager.Api.Configuration;

namespace ProjectManager.Tests;

/// <summary>
/// Configuration parsing is the seam where a deployment typo turns into
/// behaviour. Two properties matter enough to test directly:
///
/// <list type="bullet">
/// <item>it cannot crash - any input at all produces options and warnings; and</item>
/// <item>it cannot silently enable the wrong mode - a bad value falls back to a
/// documented default AND says so.</item>
/// </list>
/// </summary>
public class AppOptionsParserTests
{
    private static AppOptionsResult Parse(params (string Key, string? Value)[] pairs)
        => AppOptionsParser.Parse(pairs.ToDictionary(p => p.Key, p => p.Value));

    // ---- Totality -------------------------------------------------------

    [Fact]
    public void Parse_neverThrows_onNullInput()
    {
        var result = AppOptionsParser.Parse(null);
        Assert.NotNull(result.Options);
    }

    [Fact]
    public void Parse_neverThrows_onEmptyInput()
    {
        var result = AppOptionsParser.Parse(new Dictionary<string, string?>());
        Assert.False(result.Options.DemoMode);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("!@#$%^&*()")]
    [InlineData("true;DROP TABLE Projects")]
    public void Parse_neverThrows_onGarbageValues(string value)
    {
        var result = AppOptionsParser.Parse(new Dictionary<string, string?>
        {
            ["DEMO_MODE"] = value,
            ["ENABLE_SWAGGER"] = value,
            ["LOG_LEVEL"] = value,
            ["CORS_ALLOWED_ORIGINS"] = value,
            ["DATABASE_PATH"] = value,
        });

        Assert.NotNull(result.Options);
        Assert.NotNull(result.Options.AllowedCorsOrigins);
    }

    [Fact]
    public void Parse_neverThrows_onNullValues()
    {
        var result = Parse(("DEMO_MODE", null), ("LOG_LEVEL", null), ("CORS_ALLOWED_ORIGINS", null));
        Assert.NotNull(result.Options);
        Assert.Empty(result.Warnings);
    }

    // ---- Demo mode: the flag that must never flip by accident ------------

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public void DemoMode_isOn_forRecognisedTruthyValues(string value)
        => Assert.True(Parse(("DEMO_MODE", value)).Options.DemoMode);

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("off")]
    public void DemoMode_isOff_forRecognisedFalseyValues(string value)
        => Assert.False(Parse(("DEMO_MODE", value)).Options.DemoMode);

    [Theory]
    [InlineData("ture")]     // the realistic typo
    [InlineData("treu")]
    [InlineData("enabled")]
    [InlineData("y")]
    public void DemoMode_failsClosedAndWarns_onATypo(string typo)
    {
        // Failing closed matters in this direction specifically: an unrecognised
        // value must not turn demo mode ON for a personal instance, and it must
        // not do so quietly.
        var result = Parse(("DEMO_MODE", typo));

        Assert.False(result.Options.DemoMode);
        Assert.Contains(result.Warnings, w => w.Contains("DEMO_MODE"));
    }

    // ---- Database namespace separation -----------------------------------

    [Fact]
    public void DatabasePath_isTheDemoFile_whenDemoModeIsOn()
        => Assert.Equal(AppOptionsParser.DemoDatabasePath, Parse(("DEMO_MODE", "true")).Options.DatabasePath);

    [Fact]
    public void DatabasePath_isThePersonalFile_whenDemoModeIsOff()
        => Assert.Equal(AppOptionsParser.PersonalDatabasePath, Parse(("DEMO_MODE", "false")).Options.DatabasePath);

    [Fact]
    public void DemoAndPersonalDatabasesAreDifferentFiles()
        => Assert.NotEqual(AppOptionsParser.DemoDatabasePath, AppOptionsParser.PersonalDatabasePath);

    [Fact]
    public void DatabasePath_overrideIsIgnoredInDemoMode_soDemoSeedingCanNeverTargetRealData()
    {
        // This is barrier #2 of the demo-data guarantee, asserted rather than
        // trusted: with demo mode on there is no configuration value that can
        // point the seeder at the personal database.
        var result = Parse(
            ("DEMO_MODE", "true"),
            ("DATABASE_PATH", AppOptionsParser.PersonalDatabasePath));

        Assert.Equal(AppOptionsParser.DemoDatabasePath, result.Options.DatabasePath);
        Assert.Contains(result.Warnings, w => w.Contains("ignored"));
    }

    [Fact]
    public void DatabasePath_overrideIsHonoured_whenDemoModeIsOff()
    {
        var result = Parse(("DEMO_MODE", "false"), ("DATABASE_PATH", "/data/mine.db"));

        Assert.Equal("/data/mine.db", result.Options.DatabasePath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ConnectionString_isDerivedFromTheDatabasePath()
        => Assert.Equal("Data Source=demo.db", Parse(("DEMO_MODE", "true")).Options.ConnectionString);

    // ---- CORS ------------------------------------------------------------

    [Fact]
    public void Cors_defaultsToLocalDevelopmentOrigins_whenUnset()
    {
        var origins = AppOptionsParser.Parse(null).Options.AllowedCorsOrigins;
        Assert.Contains("http://localhost:5174", origins);
    }

    [Fact]
    public void Cors_parsesACommaSeparatedList()
    {
        var origins = Parse(("CORS_ALLOWED_ORIGINS", "https://a.example, https://b.example")).Options.AllowedCorsOrigins;
        Assert.Equal(new[] { "https://a.example", "https://b.example" }, origins.ToArray());
    }

    [Fact]
    public void Cors_stripsTrailingSlashes_becauseBrowsersCompareBareOrigins()
    {
        var origins = Parse(("CORS_ALLOWED_ORIGINS", "https://a.example/")).Options.AllowedCorsOrigins;
        Assert.Equal(new[] { "https://a.example" }, origins.ToArray());
    }

    [Fact]
    public void Cors_stripsPathsFromAnOrigin()
    {
        var origins = Parse(("CORS_ALLOWED_ORIGINS", "https://user.github.io/ProjectManager/")).Options.AllowedCorsOrigins;
        Assert.Equal(new[] { "https://user.github.io" }, origins.ToArray());
    }

    [Fact]
    public void Cors_dropsAWildcardAndWarns()
    {
        // An unauthenticated write API must not be able to acquire wildcard CORS
        // through a configuration value.
        var result = Parse(("CORS_ALLOWED_ORIGINS", "*"));

        Assert.DoesNotContain("*", result.Options.AllowedCorsOrigins);
        Assert.Contains(result.Warnings, w => w.Contains("'*'"));
    }

    [Fact]
    public void Cors_keepsTheValidEntries_whenOnlySomeAreBad()
    {
        var result = Parse(("CORS_ALLOWED_ORIGINS", "not-a-url, https://good.example, *"));

        Assert.Equal(new[] { "https://good.example" }, result.Options.AllowedCorsOrigins.ToArray());
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Cors_fallsBackToDefaults_whenNothingUsableSurvives()
    {
        var result = Parse(("CORS_ALLOWED_ORIGINS", "nonsense, also-nonsense"));

        Assert.Contains("http://localhost:5174", result.Options.AllowedCorsOrigins);
        Assert.Contains(result.Warnings, w => w.Contains("no usable origins"));
    }

    [Fact]
    public void Cors_deduplicatesRepeatedOrigins()
    {
        var origins = Parse(("CORS_ALLOWED_ORIGINS", "https://a.example, https://a.example/")).Options.AllowedCorsOrigins;
        Assert.Single(origins);
    }

    // ---- Remaining values -------------------------------------------------

    [Fact]
    public void Swagger_isOffByDefault()
        => Assert.False(AppOptionsParser.Parse(null).Options.EnableSwagger);

    [Fact]
    public void LogLevel_defaultsToInformation()
        => Assert.Equal(LogLevel.Information, AppOptionsParser.Parse(null).Options.MinimumLogLevel);

    [Fact]
    public void LogLevel_parsesAValidLevelCaseInsensitively()
        => Assert.Equal(LogLevel.Warning, Parse(("LOG_LEVEL", "warning")).Options.MinimumLogLevel);

    [Fact]
    public void LogLevel_fallsBackAndWarns_onAnInvalidLevel()
    {
        var result = Parse(("LOG_LEVEL", "chatty"));

        Assert.Equal(LogLevel.Information, result.Options.MinimumLogLevel);
        Assert.Contains(result.Warnings, w => w.Contains("LOG_LEVEL"));
    }

    [Fact]
    public void BuildMetadata_hasHonestPlaceholders_whenNotInjected()
    {
        var options = AppOptionsParser.Parse(null).Options;

        Assert.Equal("unknown", options.BuildCommit);
        Assert.Equal("dev", options.BuildVersion);
    }

    [Fact]
    public void BuildMetadata_isReadFromTheEnvironment_whenInjected()
    {
        var options = Parse(("BUILD_COMMIT", "abc1234"), ("BUILD_VERSION", "2026.08.20")).Options;

        Assert.Equal("abc1234", options.BuildCommit);
        Assert.Equal("2026.08.20", options.BuildVersion);
    }
}
