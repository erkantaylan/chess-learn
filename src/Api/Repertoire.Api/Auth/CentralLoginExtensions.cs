using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Repertoire.Api.Auth;

/// <summary>
/// Sign-in through the central identity provider (authentik), authorization code + PKCE.
///
/// The IdP is visited only at sign-in: what comes back is turned into the same repertoire.auth
/// cookie the password login issues, and from then on the app never talks to the IdP again
/// until /logout. It is deliberately not the default challenge scheme — /api/* keeps answering
/// 401/403, and only a page navigation to /login is sent to the IdP.
/// </summary>
public static class CentralLoginExtensions
{
    public static AuthenticationBuilder AddCentralLogin(this AuthenticationBuilder authentication, OidcOptions oidc) =>
        authentication.AddOpenIdConnect(options =>
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

            // Needed for the id_token_hint on RP-initiated logout; see OnTicketReceived for why
            // that is the only token kept.
            options.SaveTokens = true;

            options.Events.OnTokenValidated = context =>
            {
                // Defense in depth: the IdP already refuses anyone without a role in this app.
                if (context.Principal?.IsInRole(OidcOptions.RequiredRole) != true)
                {
                    context.Fail($"no '{OidcOptions.RequiredRole}' role for this app");
                }

                return Task.CompletedTask;
            };

            options.Events.OnTicketReceived = context =>
            {
                // The cookie would otherwise carry access + refresh tokens the app never uses,
                // pushing it into several chunks. Logout needs the id_token alone.
                string? idToken = context.Properties?.GetTokenValue(OpenIdConnectParameterNames.IdToken);
                context.Properties?.StoreTokens(idToken is null
                    ? []
                    : [new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken }]);
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

    /// <summary>
    /// With an authority, studies need the IdP's role as well as a session. Without one, the
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
