using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Repertoire.Api.Auth;

namespace Repertoire.Api.StaticApp;

/// <summary>
/// Sign-in lives on its own page. The board itself never mentions it: anonymous visitors get a
/// fully working, entirely in-memory app with no login prompt, no hint and nothing to dismiss.
/// You go to /login because you know it is there.
///
/// With the central IdP configured, /login is not a page but a redirect to the IdP, and /logout
/// signs out there too.
/// </summary>
public static class LoginPageExtensions
{
    public static WebApplication MapLoginPage(this WebApplication app)
    {
        bool usesOidc = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.UsesOidc;

        if (usesOidc)
        {
            app.MapGet("/login", (HttpContext http, string? next) =>
               {
                   string destination = LocalPathOrRoot(next);
                   return http.User.Identity?.IsAuthenticated == true
                       ? Results.Redirect(destination)
                       : Results.Challenge(
                           new AuthenticationProperties { RedirectUri = destination },
                           [OpenIdConnectDefaults.AuthenticationScheme]);
               })
               .AllowAnonymous()
               .ExcludeFromDescription();

            // Cookie first, then the IdP's end-session endpoint (id_token_hint from the cookie's
            // saved id_token), which returns via /signout-callback-oidc to the board.
            app.MapGet("/logout", () => Results.SignOut(
                   new AuthenticationProperties { RedirectUri = "/" },
                   [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
               .AllowAnonymous()
               .ExcludeFromDescription();

            return app;
        }

        // UseStaticFiles already serves /login.html; this is the extensionless alias.
        app.MapGet("/login", (HttpContext http) =>
           {
               string root = StaticAppExtensions.ResolveRoot(app.Environment, app.Configuration);
               string file = Path.Combine(root, "login.html");
               return File.Exists(file)
                   ? Results.File(file, "text/html; charset=utf-8")
                   : Results.NotFound();
           })
           .AllowAnonymous()
           .ExcludeFromDescription();

        // Signing out from the page rather than the app: land back on the board, signed out.
        app.MapGet("/logout", async (HttpContext http) =>
           {
               await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
               return Results.Redirect("/");
           })
           .AllowAnonymous()
           .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Where to go after signing in, the same rule login.html applies to ?next=: only ever a path
    /// on this origin, or /login becomes an open redirect for anyone who can get you to click it.
    /// </summary>
    private static string LocalPathOrRoot(string? next) =>
        next is not null && next.StartsWith('/') && !next.StartsWith("//") && !next.StartsWith("/\\")
            ? next
            : "/";
}
