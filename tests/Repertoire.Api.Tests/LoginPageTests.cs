using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Repertoire.Api.Tests;

/// <summary>
/// /login and /logout, the page-navigation doors. Signing out leaves this app only — under the
/// central login too, never the IdP — and signing in returns to ?next=, a local path only.
/// </summary>
[Collection(StudiesApiCollection.Name)]
public class LoginPageTests(StudiesApiFactory app)
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    /// <summary>
    /// The app with an authority set, which switches it to the central login. Nothing here ever
    /// contacts that authority — a test that did would fail on discovery, which is the point.
    /// </summary>
    private WebApplicationFactory<RepertoireProgram> Central() =>
        app.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Oidc:Authority", "http://idp.localhost/application/o/chess-learn/");
            builder.UseSetting("Auth:Oidc:ClientId", "chess-learn");
            builder.UseSetting("Auth:Oidc:ClientSecret", "secret");
        });

    [Fact]
    public async Task Password_login_logout_clears_the_cookie_and_returns_to_the_board()
    {
        using HttpClient client = app.CreateClient(NoRedirects);
        await client.PostAsJsonAsync("/api/login", new { username = "erkan", password = "chess" });

        HttpResponseMessage logout = await client.GetAsync("/logout");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/studies")).StatusCode);
    }

    [Fact]
    public async Task Central_login_logout_does_not_go_to_the_identity_provider()
    {
        // A sign-out that went on to the IdP would need its discovery document, and fail or
        // redirect there.
        using WebApplicationFactory<RepertoireProgram> central = Central();
        using HttpClient client = central.CreateClient(NoRedirects);

        // Proof the switch took: the password door is gone under the central login.
        Assert.Equal(HttpStatusCode.NotFound,
                     (await client.PostAsJsonAsync("/api/login", new { username = "erkan", password = "chess" })).StatusCode);

        HttpResponseMessage logout = await client.GetAsync("/logout");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/studies?x=1", "/studies?x=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    public async Task Central_login_returns_a_signed_in_caller_to_next_if_it_is_local(string next, string expected)
    {
        using WebApplicationFactory<RepertoireProgram> central = Central();
        using HttpClient client = central.CreateClient(NoRedirects);
        client.DefaultRequestHeaders.Add(StudiesApiFactory.CentralSubHeader, "sub-signed-in");

        HttpResponseMessage response = await client.GetAsync($"/login?next={Uri.EscapeDataString(next)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Password_login_page_is_served_at_login_with_next_left_for_the_page()
    {
        using HttpClient client = app.CreateClient(NoRedirects);

        HttpResponseMessage response = await client.GetAsync("/login?next=/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("function destination()", await response.Content.ReadAsStringAsync());
    }
}
