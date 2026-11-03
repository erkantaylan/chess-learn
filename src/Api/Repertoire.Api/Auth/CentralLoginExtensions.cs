using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Repertoire.Api.Auth;

/// <summary>
/// Sign-in through the central identity provider (authentik), authorization code + PKCE.
///
/// What comes back is turned into the same repertoire.auth cookie the password login issues. After
/// that the IdP is asked again only every Auth:Oidc:RecheckSeconds, by refresh-token grant, so
/// roles stay current (<see cref="RoleRecheckService"/>). It is deliberately not the default
/// challenge scheme — /api/* keeps answering 401/403, and only a page navigation to /login is
/// sent to the IdP.
/// </summary>
public static class CentralLoginExtensions
{
    public static AuthenticationBuilder AddCentralLogin(this AuthenticationBuilder authentication, OidcOptions oidc)
    {
        authentication.Services.AddSingleton<RoleRecheckService>();
        authentication.Services.Configure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            options => options.Events.OnValidatePrincipal = context =>
                context.HttpContext.RequestServices.GetRequiredService<RoleRecheckService>().ValidateAsync(context));

        return authentication.AddOpenIdConnect(options =>
        {
            options.Authority = oidc.Authority;
            options.ClientId = oidc.ClientId;
            options.ClientSecret = oidc.ClientSecret;
            // Plain-HTTP discovery is only ever acceptable for a local IdP. Anywhere else an http
            // authority fails at the first sign-in rather than fetching signing keys in the clear.
            options.RequireHttpsMetadata = !IsLocalHttp(oidc.Authority);

            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;

            options.Scope.Clear();
            foreach (string scope in new[] { "openid", "profile", "email", "entitlements", "offline_access" })
            {
                options.Scope.Add(scope);
            }

            // Claims exactly as the IdP names them (sub, preferred_username, roles), all from the
            // id_token — no userinfo round trip.
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.TokenValidationParameters.RoleClaimType = "roles";

            // The id_token for the id_token_hint on RP-initiated logout, the refresh token for the
            // role re-check; see OnTicketReceived for why those are the only tokens kept.
            options.SaveTokens = true;

            options.Events.OnTokenValidated = context =>
            {
                // Defense in depth: the IdP gives every signed-in user "guest", so anyone without
                // even that is someone it should not have let through.
                if (context.Principal?.Claims.Any(c => c.Type == "roles" && OidcOptions.SignInRoles.Contains(c.Value)) != true)
                {
                    context.Fail($"neither '{OidcOptions.RequiredRole}' nor '{OidcOptions.GuestRole}' role for this app");
                }

                return Task.CompletedTask;
            };

            options.Events.OnTicketReceived = context =>
            {
                // The access token is never used (roles come from the id_token), and it would push
                // the cookie into yet another chunk. Keep id_token + refresh token, and stamp the
                // sign-in as the first role check.
                if (context.Properties is not null)
                {
                    RoleRecheckService.PrepareSignInProperties(context.Properties);
                }

                return Task.CompletedTask;
            };

            options.Events.OnRemoteFailure = async context =>
            {
                // A refused or broken callback would otherwise surface as an unhandled exception.
                ILogger logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                        .CreateLogger(typeof(CentralLoginExtensions));
                logger.LogWarning(context.Failure, "Central sign-in failed");

                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Sign-in was refused. Go back to the board: /");
            };
        });
    }

    /// <summary>
    /// With an authority, studies need the IdP's "user" role as well as a session — a guest is
    /// signed in but gets 403. Without one, the
    /// password login's cookie has no roles at all, so the policy stays "signed in".
    /// </summary>
    public static void RequireCentralRole(this AuthorizationOptions options) =>
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
                                .RequireAuthenticatedUser()
                                .RequireRole(OidcOptions.RequiredRole)
                                .Build();

    private static bool IsLocalHttp(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase));
}
