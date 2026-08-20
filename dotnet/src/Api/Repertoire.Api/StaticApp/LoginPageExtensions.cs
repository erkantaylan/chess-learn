using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Repertoire.Api.StaticApp;

/// <summary>
/// Sign-in lives on its own page. The board itself never mentions it: anonymous visitors get a
/// fully working, entirely in-memory app with no login prompt, no hint and nothing to dismiss.
/// You go to /login because you know it is there.
/// </summary>
public static class LoginPageExtensions
{
    public static WebApplication MapLoginPage(this WebApplication app)
    {
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
}
