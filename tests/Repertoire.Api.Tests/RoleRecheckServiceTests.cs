using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Repertoire.Api.Auth;

namespace Repertoire.Api.Tests;

/// <summary>
/// The re-check end to end against a fake IdP: a stub token endpoint behind the handler's
/// backchannel. "Unreachable" is a backchannel that throws, so the shared IdP is never touched.
/// </summary>
public class RoleRecheckServiceTests
{
    private const string TokenEndpoint = "http://idp.test/application/o/token/";

    [Fact]
    public async Task Unreachable_IdP_keeps_the_session_on_its_current_roles()
    {
        FakeIdp idp = new(_ => throw new HttpRequestException("connection refused"));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400);

        await Service(idp).ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.True(context.Principal!.IsInRole("user"));
        Assert.False(context.ShouldRenew);
        Assert.Equal(1, idp.Calls);
    }

    [Fact]
    public async Task Unreachable_IdP_ends_the_session_once_offline_too_long()
    {
        FakeIdp idp = new(_ => throw new HttpRequestException("connection refused"));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 3601);

        await Service(idp).ValidateAsync(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Unreachable_IdP_is_retried_on_the_next_request()
    {
        FakeIdp idp = new(_ => throw new HttpRequestException("connection refused"));
        RoleRecheckService service = Service(idp);

        await service.ValidateAsync(Context(checkedSecondsAgo: 400));
        await service.ValidateAsync(Context(checkedSecondsAgo: 400));

        Assert.Equal(2, idp.Calls);
    }

    [Fact]
    public async Task A_5xx_keeps_the_session()
    {
        FakeIdp idp = new(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400);

        await Service(idp).ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.False(context.ShouldRenew);
    }

    [Fact]
    public async Task Invalid_grant_ends_the_session()
    {
        FakeIdp idp = new(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400);

        await Service(idp).ValidateAsync(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task A_200_replaces_the_roles_and_rotates_the_refresh_token()
    {
        string idToken = RoleRecheckTests.Jwt("""{"sub":"1","roles":["guest"]}""");
        FakeIdp idp = new(_ => Json(HttpStatusCode.OK,
            $$"""{"access_token":"a","id_token":"{{idToken}}","refresh_token":"rt-2","token_type":"Bearer"}"""));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400);

        await Service(idp).ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.False(context.Principal!.IsInRole("user"));
        Assert.True(context.Principal.IsInRole("guest"));
        Assert.True(context.ShouldRenew);
        Assert.Equal("rt-2", context.Properties.GetTokenValue("refresh_token"));
        Assert.Equal(idToken, context.Properties.GetTokenValue("id_token"));
        Assert.Null(context.Properties.GetTokenValue("access_token"));
        Assert.Contains("refresh_token=rt-1", idp.LastForm);
    }

    [Fact]
    public async Task A_200_without_a_role_for_this_app_ends_the_session()
    {
        string idToken = RoleRecheckTests.Jwt("""{"sub":"1","roles":[]}""");
        FakeIdp idp = new(_ => Json(HttpStatusCode.OK, $$"""{"id_token":"{{idToken}}","refresh_token":"rt-2"}"""));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400);

        await Service(idp).ValidateAsync(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Not_due_means_no_call()
    {
        FakeIdp idp = new(_ => throw new InvalidOperationException("should not be called"));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 10);

        await Service(idp).ValidateAsync(context);

        Assert.NotNull(context.Principal);
        Assert.Equal(0, idp.Calls);
    }

    [Fact]
    public async Task No_refresh_token_ends_the_session_without_asking()
    {
        FakeIdp idp = new(_ => throw new InvalidOperationException("should not be called"));
        CookieValidatePrincipalContext context = Context(checkedSecondsAgo: 400, refreshToken: null);

        await Service(idp).ValidateAsync(context);

        Assert.Null(context.Principal);
        Assert.Equal(0, idp.Calls);
    }

    [Fact]
    public async Task Concurrent_requests_with_one_refresh_token_make_one_grant()
    {
        // The IdP rotates refresh tokens: a second grant with rt-1 would be invalid_grant.
        TaskCompletionSource release = new();
        string idToken = RoleRecheckTests.Jwt("""{"sub":"1","roles":["user","guest"]}""");
        FakeIdp idp = new(_ =>
        {
            release.Task.Wait();
            return Json(HttpStatusCode.OK, $$"""{"id_token":"{{idToken}}","refresh_token":"rt-2"}""");
        });
        RoleRecheckService service = Service(idp);
        CookieValidatePrincipalContext[] contexts = [.. Enumerable.Range(0, 5).Select(_ => Context(checkedSecondsAgo: 400))];

        Task all = Task.WhenAll(contexts.Select(c => Task.Run(() => service.ValidateAsync(c))));
        await Task.Delay(100);
        release.SetResult();
        await all;

        Assert.Equal(1, idp.Calls);
        Assert.All(contexts, c => Assert.Equal("rt-2", c.Properties.GetTokenValue("refresh_token")));
        Assert.All(contexts, c => Assert.NotNull(c.Principal));
    }

    private static RoleRecheckService Service(FakeIdp idp)
    {
        OpenIdConnectOptions handler = new()
        {
            Backchannel = new HttpClient(idp),
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { TokenEndpoint = TokenEndpoint }),
        };
        AuthOptions auth = new()
        {
            Oidc = new OidcOptions
            {
                Authority = "http://idp.test/application/o/chess-learn/",
                ClientId = "chess-learn",
                ClientSecret = "secret",
                RecheckSeconds = 300,
                MaxOfflineSeconds = 3600,
            },
        };
        return new RoleRecheckService(new FixedMonitor(handler), Options.Create(auth), NullLogger<RoleRecheckService>.Instance);
    }

    private static CookieValidatePrincipalContext Context(int checkedSecondsAgo, string? refreshToken = "rt-1")
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };

        ClaimsIdentity identity = new(
            [new Claim("preferred_username", "friend1"), new Claim("roles", "user"), new Claim("roles", "guest")],
            "AuthenticationTypes.Federation", "preferred_username", "roles");

        AuthenticationProperties properties = new();
        List<AuthenticationToken> tokens = [new() { Name = "id_token", Value = "old-id-token" }];
        if (refreshToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = refreshToken });
        }

        properties.StoreTokens(tokens);
        properties.Items[RoleRecheck.CheckedAtItem] =
            DateTimeOffset.UtcNow.AddSeconds(-checkedSecondsAgo).ToUnixTimeSeconds().ToString();

        AuthenticationScheme scheme = new(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(),
            new AuthenticationTicket(new ClaimsPrincipal(identity), properties, scheme.Name));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class FakeIdp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        public string LastForm { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Assert.Equal(TokenEndpoint, request.RequestUri!.ToString());
            LastForm = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class FixedMonitor(OpenIdConnectOptions value) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue => value;

        public OpenIdConnectOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
    }
}
