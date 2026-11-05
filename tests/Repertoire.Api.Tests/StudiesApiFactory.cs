using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Repertoire.Api.Auth;
using Testcontainers.PostgreSql;

namespace Repertoire.Api.Tests;

/// <summary>One app and one Postgres container for every test class that needs them.</summary>
[CollectionDefinition(Name)]
public sealed class StudiesApiCollection : ICollectionFixture<StudiesApiFactory>
{
    public const string Name = "studies api";
}

/// <summary>
/// The real app — RepertoireProgram.Main, migrations and all — on a throwaway Postgres in Docker.
/// Development environment, so the password login is on with appsettings.Development.json's pair.
///
/// Signing in as several people at once is what ownership tests need and what neither real login
/// offers (one configured account; an IdP that is not here). So a request carrying
/// <see cref="CentralSubHeader"/> or <see cref="PasswordUserHeader"/> is authenticated as a
/// principal of exactly that mode's shape; any other request falls through to the real cookie.
/// </summary>
public sealed class StudiesApiFactory : WebApplicationFactory<RepertoireProgram>, IAsyncLifetime
{
    /// <summary>Signs the request in as a central-login user with this <c>sub</c>.</summary>
    public const string CentralSubHeader = "X-Test-Sub";

    /// <summary>Signs the request in as the password login's user with this name.</summary>
    public const string PasswordUserHeader = "X-Test-Name";

    private const string TestScheme = "Test";
    private const string TestOrCookie = "TestOrCookie";

    // The version compose and Coolify run.
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17.6").Build();

    public string ConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync() => await _db.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>A client signed in as a central-login user.</summary>
    public HttpClient AsCentral(string sub)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(CentralSubHeader, sub);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:cs-repertoire", _db.GetConnectionString());
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(TestScheme, _ => { })
                    .AddPolicyScheme(TestOrCookie, TestOrCookie, options => options.ForwardDefaultSelector = context =>
                        context.Request.Headers.ContainsKey(CentralSubHeader) || context.Request.Headers.ContainsKey(PasswordUserHeader)
                            ? TestScheme
                            : CookieAuthenticationDefaults.AuthenticationScheme);

            services.PostConfigure<AuthenticationOptions>(options => options.DefaultScheme = TestOrCookie);
        });
    }

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsPrincipal principal = Request.Headers.TryGetValue(CentralSubHeader, out var sub)
                ? StudyOwnerTests.Central(sub.ToString(), username: "same-display-name")
                : StudyOwnerTests.Password(Request.Headers[PasswordUserHeader].ToString());

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
        }
    }
}
