using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        // Anonymous by design — you cannot sign in through a door that needs you signed in.
        routes.MapGet("/me", (ClaimsPrincipal user) =>
                  Results.Ok(new MeResponse(
                      user.Identity?.IsAuthenticated ?? false,
                      user.Identity?.Name)))
              .AllowAnonymous()
              .WithName("Me");

        routes.MapPost("/login", async (LoginRequest body, HttpContext http, IOptions<AuthOptions> options) =>
              {
                  AuthOptions auth = options.Value;

                  if (!auth.IsConfigured)
                  {
                      return Results.Problem(
                          detail: "No account is configured on this server (set Auth__Username and Auth__Password).",
                          statusCode: StatusCodes.Status503ServiceUnavailable);
                  }

                  bool ok = FixedTimeEquals(body.Username, auth.Username)
                            & FixedTimeEquals(body.Password, auth.Password);

                  if (!ok)
                  {
                      return Results.Problem(
                          detail: "Wrong username or password",
                          statusCode: StatusCodes.Status401Unauthorized);
                  }

                  ClaimsPrincipal principal = new(new ClaimsIdentity(
                      [new Claim(ClaimTypes.Name, auth.Username)],
                      CookieAuthenticationDefaults.AuthenticationScheme));

                  await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                  return Results.Ok(new MeResponse(true, auth.Username));
              })
              .AllowAnonymous()
              .WithName("Login");

        routes.MapPost("/logout", async (HttpContext http) =>
              {
                  await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                  return Results.Ok(new MeResponse(false, null));
              })
              .AllowAnonymous()
              .WithName("Logout");

        return routes;
    }

    /// <summary>
    /// Compares in time independent of where the strings diverge, so a wrong password cannot be
    /// guessed a character at a time from response timing. Both legs of the credential check run
    /// unconditionally (note the non-short-circuiting &amp; at the call site) for the same reason.
    /// </summary>
    private static bool FixedTimeEquals(string? candidate, string? expected)
    {
        // Hash first so the comparison is over two 32-byte digests. CryptographicOperations
        // .FixedTimeEquals returns early when lengths differ, which would otherwise leak the
        // length of the real password.
        Span<byte> a = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> b = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? string.Empty), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? string.Empty), b);

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
