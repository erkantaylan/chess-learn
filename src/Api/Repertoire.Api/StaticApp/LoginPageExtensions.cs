using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Repertoire.Api.Auth;

namespace Repertoire.Api.StaticApp;

/// <summary>
/// Sign-in lives on its own page, and the board links to it: signed out, it shows a Sign in
/// button that goes to /login?next=/. Nothing more than that — anonymous visitors still get a
/// fully working, entirely in-memory app with no prompt to dismiss; the button is there for
/// whoever wants their studies kept, now that each account has a library of its own.
///
/// With the central IdP configured, /login is not a page but a redirect to the IdP. Either way
/// ?next= (a path on this origin) is where you land afterwards, and /logout signs you out of this
/// app only — never out of the IdP, where you may be signed in to other apps too.
/// </summary>
public static class LoginPageExtensions
{
    public static WebApplication MapLoginPage(this WebApplication app)
    {
        bool usesOidc = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.UsesOidc;

        // Signing out from a link rather than the in-app button: land back on the board, signed
        // out. The cookie only, in both modes. Under the central login this used to go on to the
        // IdP's end-session endpoint as well, which signed you out of every app on it; leaving
        // this app is all a Sign out button here should do. The IdP session stays, so the next
        // /login is one click with no password.
        app.MapGet("/logout", async (HttpContext http) =>
           {
               await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
               return Results.Redirect("/");
           })
           .AllowAnonymous()
           .ExcludeFromDescription();

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
