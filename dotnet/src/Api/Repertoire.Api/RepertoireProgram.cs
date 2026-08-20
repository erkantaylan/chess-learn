using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Repertoire.Api.Auth;
using Repertoire.Api.StaticApp;
using Repertoire.Api.Studies;
using Scalar.AspNetCore;

namespace Repertoire.Api;

public class RepertoireProgram
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // OpenTelemetry (traces/metrics/logs over OTLP to the Aspire dashboard), service
        // discovery, HTTP resilience and the /health + /alive probes.
        builder.AddServiceDefaults();

        // Connection string name "cs-repertoire" is the Aspire resource name — the AppHost injects
        // ConnectionStrings__cs-repertoire, and so does docker-compose.
        builder.AddNpgsqlDbContext<StudyDbContext>("cs-repertoire");

        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

        // Behind Coolify/Traefik, TLS is terminated at the proxy and the request reaches Kestrel as
        // plain HTTP. Without this the app believes it is serving HTTP, and the session cookie below
        // never gets its Secure flag on a site that is HTTPS from the browser's point of view.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // In a container the proxy's address is whatever the platform assigns, so there is no
            // stable IP to allow-list. Safe here because the container publishes no host port —
            // compose uses `expose`, so the platform's proxy is the only thing that can reach it.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
               .AddCookie(options =>
               {
                   options.Cookie.Name = "repertoire.auth";
                   options.Cookie.HttpOnly = true;
                   options.Cookie.SameSite = SameSiteMode.Lax;
                   // Secure whenever the request arrived over HTTPS, so the cookie still works on
                   // plain-HTTP localhost but is never sent in the clear once there is TLS.
                   options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                   options.ExpireTimeSpan = TimeSpan.FromDays(30);
                   options.SlidingExpiration = true;

                   // This is an API, not a server-rendered site: answer with a status code rather
                   // than a 302 to a /Account/Login page that does not exist. Without this the
                   // frontend's fetch() sees an opaque redirect instead of a clean 401.
                   options.Events.OnRedirectToLogin = context =>
                   {
                       context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                       return Task.CompletedTask;
                   };
                   options.Events.OnRedirectToAccessDenied = context =>
                   {
                       context.Response.StatusCode = StatusCodes.Status403Forbidden;
                       return Task.CompletedTask;
                   };
               });

        builder.Services.AddAuthorization();

        // snake_case on the wire: start_fen, created_at, move_count. The contract predates this
        // rewrite (docs/API.md) and the frontend reads those names verbatim.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            // Move trees are deep; the default of 64 rejects real studies. See MaxJsonDepth.
            options.SerializerOptions.MaxDepth = StudyMapping.MaxJsonDepth;
        });

        builder.Services.AddOpenApi();

        WebApplication app = builder.Build();

        RequireConfiguredAccount(app);

        await MigrateDatabaseAsync(app);

        // First in the pipeline: everything downstream that asks "was this HTTPS?" — the auth
        // cookie above all — needs the real scheme, not the proxy's hop.
        app.UseForwardedHeaders();

        app.UseCrossOriginIsolationAndNoCache();

        app.UseAuthentication();
        app.UseAuthorization();

        RouteGroupBuilder api = app.MapGroup("/api");
        api.MapAuthEndpoints();
        api.MapStudyEndpoints();

        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        // Last, so /api/* always wins.
        app.UseRepertoireStaticApp();
        app.MapLoginPage();

        await app.RunAsync();
    }

    /// <summary>
    /// Refuses to boot outside Development without credentials. Deployed with no account
    /// configured, the app would come up looking healthy while every save silently 401s — better
    /// to fail at startup, where the message is right there in the logs.
    /// </summary>
    private static void RequireConfiguredAccount(WebApplication app)
    {
        AuthOptions auth = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (auth.IsConfigured)
        {
            return;
        }

        const string message = "No account configured. Set Auth__Username and Auth__Password.";

        if (app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning("{Message} Sign-in is unavailable until you do.", message);
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Applies EF migrations on startup. One user, one instance — there is no rolling deploy to
    /// race with, so the simple thing is the correct thing here.
    /// </summary>
    private static async Task MigrateDatabaseAsync(WebApplication app)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        StudyDbContext db = scope.ServiceProvider.GetRequiredService<StudyDbContext>();
        await db.Database.MigrateAsync();
    }
}
