using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
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

        // The keys the auth cookie is encrypted with live in that same database, so a redeploy
        // (a fresh container) does not sign everyone out.
        builder.Services.AddRepertoireDataProtection();

        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        // Read once up front as well: which handlers exist has to be decided before Build().
        AuthOptions authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
                                  ?? new AuthOptions();

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

        AuthenticationBuilder authentication =
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

        if (authOptions.UsesOidc)
        {
            authentication.AddCentralLogin(authOptions.Oidc);
        }

        builder.Services.AddAuthorization(options =>
        {
            if (authOptions.UsesOidc)
            {
                options.RequireCentralRole();
            }
        });

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
    /// Refuses to boot outside Development without credentials — either the password pair or a
    /// complete OIDC client. Deployed with no account configured, the app would come up looking
    /// healthy while every save silently 401s — better to fail at startup, where the message is
    /// right there in the logs. A half-configured OIDC client fails everywhere, Development
    /// included: with an authority set, the OIDC handler runs on every request and would 500 them all.
    /// </summary>
    private static void RequireConfiguredAccount(WebApplication app)
    {
        AuthOptions auth = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (auth.IsConfigured)
        {
            return;
        }

        if (auth.UsesOidc)
        {
            throw new InvalidOperationException(
                "Auth__Oidc__Authority is set but the client is not. Set Auth__Oidc__ClientId and Auth__Oidc__ClientSecret.");
        }

        const string message = "No account configured. Set Auth__Username and Auth__Password, or Auth__Oidc__Authority "
                               + "with Auth__Oidc__ClientId and Auth__Oidc__ClientSecret.";

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
