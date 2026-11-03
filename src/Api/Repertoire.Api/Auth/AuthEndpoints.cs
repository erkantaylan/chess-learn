using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        // Anonymous by design — you cannot sign in through a door that needs you signed in.
        // can_save is the very policy /api/studies enforces, so a guest learns "signed in, but
        // nothing will be stored" up front instead of from a 403 on the first save.
        routes.MapGet("/me", async (ClaimsPrincipal user, IAuthorizationService authorization,
                                    IAuthorizationPolicyProvider policies) =>
              {
                  bool authed = user.Identity?.IsAuthenticated ?? false;
                  if (!authed)
                  {
                      return Results.Ok(MeResponse.SignedOut);
                  }

                  AuthorizationPolicy studiesPolicy = await policies.GetDefaultPolicyAsync();
                  bool canSave = (await authorization.AuthorizeAsync(user, studiesPolicy)).Succeeded;
                  string[] roles = user.Identities
                                       .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
                                       .Select(claim => claim.Value)
                                       .Distinct(StringComparer.Ordinal)
                                       .ToArray();

                  return Results.Ok(new MeResponse(true, user.Identity?.Name, roles, canSave));
              })
              .AllowAnonymous()
              .WithName("Me");

        // With the central IdP configured, the password door does not exist at all (404), rather
        // than existing and refusing — /login sends you to the IdP instead.
        if (!routes.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value.UsesOidc)
        {
            MapPasswordLogin(routes);
        }

        // Local only, under OIDC too: the in-app button keeps the board on screen, so it cannot
        // navigate away to the IdP. GET /logout is the full sign-out.
        routes.MapPost("/logout", async (HttpContext http) =>
              {
                  await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                  return Results.Ok(MeResponse.SignedOut);
              })
              .AllowAnonymous()
              .WithName("Logout");

        return routes;
    }

    private static void MapPasswordLogin(IEndpointRouteBuilder routes)
    {
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

                  return Results.Ok(new MeResponse(true, auth.Username, [], true));
              })
              .AllowAnonymous()
              .WithName("Login");
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
