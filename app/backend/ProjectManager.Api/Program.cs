using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Api.Configuration;
using ProjectManager.Api.Data;
using ProjectManager.Api.Demo;
using ProjectManager.Api.Middleware;
using ProjectManager.Api.Services;

// ---------------------------------------------------------------------------
// Composition root. Every dependency the app uses is constructed here and only
// here; nothing further in reaches for configuration or news up its own
// collaborators. If you want to know what this application is made of, this
// file is the entire answer.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Configuration is parsed once, up front, into an immutable object. Parsing is
// pure and total: bad input degrades to a documented default and produces a
// warning rather than throwing, so a typo in an environment variable cannot
// take the process down at boot - see AppOptionsParser.
var (options, configWarnings) = AppOptionsParser.FromEnvironment();

builder.Logging.SetMinimumLevel(options.MinimumLogLevel);

// EF Core logs every statement it executes at Information. It redacts parameter
// values by default - verified, not assumed: nothing from a project name or an
// action description appears in the output - but the volume buries the handful
// of lines worth reading, and closing the channel removes a place where a future
// EnableSensitiveDataLogging could quietly start leaking row content.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);

builder.Services.AddSingleton(options);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(dbOptions =>
    dbOptions.UseSqlite(options.ConnectionString));

builder.Services.AddScoped<BlockingService>();

// CORS is an explicit allow-list read from configuration. AllowAnyOrigin is
// never used and the parser drops a literal "*": this API has no authentication,
// so a wildcard is never the intent and a config value should not be able to
// turn one on.
const string CorsPolicy = "AppFrontend";
builder.Services.AddCors(cors =>
{
    cors.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(options.AllowedCorsOrigins.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// The deployed instance is a public, unauthenticated, writable API. Rate
// limiting does not make that safe - only the demo-data strategy does - but it
// does bound how fast a single client can churn the dataset.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

foreach (var warning in configWarnings)
{
    startupLogger.LogWarning("Configuration: {Warning}", warning);
}

// Database bootstrap. Scalars only in these log lines - never row contents.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbSeeder.Seed(db);

    if (options.DemoMode)
    {
        // SeedIfEmpty, never ResetToDemo. Startup has no path that can delete
        // data: the safe operation and the destructive one are separate methods
        // precisely so this call site cannot accidentally be the other one.
        var outcome = DemoDataSeeder.SeedIfEmpty(db, DateTime.UtcNow);
        startupLogger.LogInformation(
            "Demo mode is ON. Database {DatabasePath}, seed outcome {Outcome}.",
            options.DatabasePath, outcome);
    }
    else
    {
        startupLogger.LogInformation(
            "Demo mode is OFF. Using database {DatabasePath}.", options.DatabasePath);
    }
}

startupLogger.LogInformation(
    "Started build {BuildVersion} ({BuildCommit}); {OriginCount} CORS origin(s) allowed; swagger {SwaggerState}.",
    options.BuildVersion, options.BuildCommit, options.AllowedCorsOrigins.Count,
    options.EnableSwagger ? "enabled" : "disabled");

// Must sit outermost so it also catches failures thrown inside later middleware.
app.UseAppExceptionHandling(includeDetail: app.Environment.IsDevelopment());

if (options.EnableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.MapControllers();

// Health/identity probe. Used by start.bat to tell this API apart from any other
// app that happens to be listening on the same port, by the deployment smoke
// test to prove the service actually answered, and by the frontend to show which
// build it is talking to. Reports only scalars about the app itself.
app.MapGet("/api/health", (AppOptions opts) => Results.Json(new
{
    app = "personal-coo",
    status = "ok",
    demoMode = opts.DemoMode,
    version = opts.BuildVersion,
    commit = opts.BuildCommit,
}));

app.Run();

