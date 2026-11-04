using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Repertoire.Api.Auth;

/// <summary>
/// Every <see cref="OidcOptions.RecheckSeconds"/>, a session's roles are re-read from the IdP
/// with a refresh-token grant, so a role taken away or an account disabled there ends here within
/// minutes rather than at the end of a 30-day cookie. Runs as the cookie's OnValidatePrincipal;
/// what to do with the answer is <see cref="RoleRecheck.Classify"/>.
///
/// Calls are made once per refresh token, not once per request. A page load sends a burst of
/// requests carrying the same cookie (the page, its scripts, /api/me…), and the IdP rotates the
/// refresh token on use: a second grant with the old token would come back invalid_grant and
/// sign the user out. So concurrent and shortly-later requests with the same token share one
/// answer — and all set the same new cookie. In-memory, which is fine for the one instance this
/// app runs as.
/// </summary>
public sealed class RoleRecheckService(
    IOptionsMonitor<OpenIdConnectOptions> oidcHandlerOptions,
    IOptions<AuthOptions> authOptions,
    ILogger<RoleRecheckService> logger)
{
    /// <summary>How long a settled answer is reused for requests that still carry the old token.</summary>
    private static readonly TimeSpan ShareAnswerFor = TimeSpan.FromMinutes(2);

    /// <summary>A hung IdP must not hang every page load with it.</summary>
    private static readonly TimeSpan GrantTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
        {
            return;
        }

        OidcOptions oidc = authOptions.Value.Oidc;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset lastSuccess = ReadCheckedAt(context.Properties);

        if (!RoleRecheck.IsDue(lastSuccess, now, TimeSpan.FromSeconds(oidc.RecheckSeconds)))
        {
            return;
        }

        string? refreshToken = context.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken);
        GrantResult result = refreshToken is null ? GrantResult.NoAnswer : await GrantOnceAsync(refreshToken, now);

        RecheckVerdict verdict = RoleRecheck.Classify(
            refreshToken is not null,
            result.Status,
            result.Roles,
            now - lastSuccess,
            TimeSpan.FromSeconds(oidc.MaxOfflineSeconds));

        switch (verdict)
        {
            case RecheckVerdict.Renew:
                ReplaceRoles(identity, result.Roles!);
                context.Properties.StoreTokens(Tokens(
                    result.IdToken ?? context.Properties.GetTokenValue(OpenIdConnectParameterNames.IdToken),
                    result.RefreshToken ?? refreshToken));
                StampCheckedAt(context.Properties, now);
                context.ShouldRenew = true;
                return;

            case RecheckVerdict.KeepForNow:
                logger.LogWarning(
                    "Role re-check for {User} got no usable answer (status {Status}); keeping the session on its current roles",
                    identity.Name, result.Status?.ToString(CultureInfo.InvariantCulture) ?? "none");
                return;

            case RecheckVerdict.SignOut:
            default:
                logger.LogInformation(
                    "Ending the session of {User} after a role re-check (refresh token: {HasRefreshToken}, status {Status}, roles {Roles})",
                    identity.Name, refreshToken is not null,
                    result.Status?.ToString(CultureInfo.InvariantCulture) ?? "none",
                    result.Roles is null ? "unknown" : string.Join(",", result.Roles));
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
        }
    }

    /// <summary>At sign-in: the cookie keeps the id_token (logout hint) and refresh token only.</summary>
    public static void PrepareSignInProperties(AuthenticationProperties properties)
    {
        properties.StoreTokens(Tokens(
            properties.GetTokenValue(OpenIdConnectParameterNames.IdToken),
            properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken)));
        StampCheckedAt(properties, DateTimeOffset.UtcNow);
    }

    private async Task<GrantResult> GrantOnceAsync(string refreshToken, DateTimeOffset now)
    {
        foreach ((string staleKey, Attempt stale) in _attempts)
        {
            if (now - stale.Started > ShareAnswerFor)
            {
                _attempts.TryRemove(new KeyValuePair<string, Attempt>(staleKey, stale));
            }
        }

        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        Attempt attempt = _attempts.GetOrAdd(key, _ => new Attempt(new Lazy<Task<GrantResult>>(() => GrantAsync(refreshToken)), now));
        GrantResult result = await attempt.Result.Value;

        // Only a real answer is worth sharing. After no answer, the next request should try again.
        if (!result.IsAnswer)
        {
            _attempts.TryRemove(new KeyValuePair<string, Attempt>(key, attempt));
        }

        return result;
    }

    private async Task<GrantResult> GrantAsync(string refreshToken)
    {
        // The OIDC handler's own backchannel and discovery document: the token endpoint is
        // whatever the IdP advertises, and there is no blanket retry policy on this client (a
        // retried grant would reuse a token the first attempt may already have rotated).
        OpenIdConnectOptions handler = oidcHandlerOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        OidcOptions oidc = authOptions.Value.Oidc;
        using CancellationTokenSource timeout = new(GrantTimeout);

        try
        {
            OpenIdConnectConfiguration discovery =
                await handler.ConfigurationManager!.GetConfigurationAsync(timeout.Token);

            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIdConnectGrantTypes.RefreshToken,
                ["refresh_token"] = refreshToken,
                ["client_id"] = oidc.ClientId,
                ["client_secret"] = oidc.ClientSecret,
            });
            using HttpResponseMessage response = await handler.Backchannel.PostAsync(discovery.TokenEndpoint, form, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            int status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Refresh-token grant answered {Status}: {Body}", status, Truncate(body));
                return new GrantResult(status, null, null, null);
            }

            using JsonDocument json = JsonDocument.Parse(body);
            string? idToken = StringProperty(json, "id_token");
            string? accessToken = StringProperty(json, "access_token");
            return new GrantResult(
                status,
                RoleRecheck.ReadRoles(idToken) ?? RoleRecheck.ReadRoles(accessToken),
                idToken,
                StringProperty(json, "refresh_token"));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException
                                      or JsonException or InvalidOperationException)
        {
            // InvalidOperationException: discovery failed (IDX20803, IdP unreachable).
            logger.LogWarning(e, "Role re-check could not reach the identity provider");
            return GrantResult.NoAnswer;
        }
    }

    private static void ReplaceRoles(ClaimsIdentity identity, IReadOnlyList<string> roles)
    {
        foreach (Claim claim in identity.FindAll(identity.RoleClaimType).ToList())
        {
            identity.RemoveClaim(claim);
        }

        foreach (string role in roles)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
    }

    private static AuthenticationToken[] Tokens(string? idToken, string? refreshToken) =>
    [
        .. idToken is null ? [] : new[] { new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken } },
        .. refreshToken is null ? [] : new[] { new AuthenticationToken { Name = OpenIdConnectParameterNames.RefreshToken, Value = refreshToken } },
    ];

    private static DateTimeOffset ReadCheckedAt(AuthenticationProperties properties) =>
        properties.Items.TryGetValue(RoleRecheck.CheckedAtItem, out string? value)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UnixEpoch;

    private static void StampCheckedAt(AuthenticationProperties properties, DateTimeOffset now) =>
        properties.Items[RoleRecheck.CheckedAtItem] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static string? StringProperty(JsonDocument json, string name) =>
        json.RootElement.ValueKind == JsonValueKind.Object
        && json.RootElement.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string body) => body.Length <= 200 ? body : body[..200];

    private sealed record Attempt(Lazy<Task<GrantResult>> Result, DateTimeOffset Started);

    private sealed record GrantResult(int? Status, IReadOnlyList<string>? Roles, string? IdToken, string? RefreshToken)
    {
        public static readonly GrantResult NoAnswer = new(null, null, null, null);

        /// <summary>The IdP said something definite: a 4xx, or a 200 with readable roles.</summary>
        public bool IsAnswer => Status is >= 400 and < 500 || (Status is >= 200 and < 300 && Roles is not null);
    }
}
