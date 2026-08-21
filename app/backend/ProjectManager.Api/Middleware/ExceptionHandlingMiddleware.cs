using System.Text.Json;

namespace ProjectManager.Api.Middleware;

/// <summary>
/// One consistent error shape for anything that escapes a controller, and one
/// place where unhandled failures get logged.
///
/// <para>
/// The developer/user split is the point: in development the response carries
/// the exception type and message so the person debugging can see what broke;
/// in production it carries a correlation id and nothing else, so a stack trace
/// or a SQL fragment can never reach a browser. The full detail still goes to
/// the log either way, keyed by the same id, so a report of "I saw error
/// 4f3c..." is traceable.
/// </para>
///
/// <para>
/// Only scalars are logged - an id, an exception type, a method and a path.
/// Project names, descriptions and action text are never written to logs, which
/// is what makes it safe to leave logging on in the deployed environment.
/// </para>
/// </summary>
/// <summary>
/// The single error shape every unhandled failure is reported in.
/// <c>Detail</c> is populated in development only and is null in production.
/// </summary>
public sealed record ErrorResponse(string Error, string ErrorId, string? Detail);

public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly bool _includeDetail;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        bool includeDetail)
    {
        _next = next;
        _logger = logger;
        _includeDetail = includeDetail;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("n")[..8];

            _logger.LogError(ex,
                "Unhandled exception {ErrorId} for {Method} {Path} ({ExceptionType})",
                errorId, context.Request.Method, context.Request.Path.Value, ex.GetType().Name);

            if (context.Response.HasStarted)
            {
                // Too late to replace the response; the log above is the record.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var payload = new ErrorResponse(
                Error: "An unexpected error occurred.",
                ErrorId: errorId,
                Detail: _includeDetail ? $"{ex.GetType().Name}: {ex.Message}" : null);

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseAppExceptionHandling(this IApplicationBuilder app, bool includeDetail)
        => app.UseMiddleware<ExceptionHandlingMiddleware>(includeDetail);
}
