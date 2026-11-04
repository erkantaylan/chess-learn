using Microsoft.IdentityModel.JsonWebTokens;

namespace Repertoire.Api.Auth;

/// <summary>What to do with a session after asking the IdP about it again.</summary>
public enum RecheckVerdict
{
    /// <summary>The IdP vouched for the user: take its roles and the rotated refresh token.</summary>
    Renew,

    /// <summary>The IdP said no, or has been silent too long: end the session.</summary>
    SignOut,

    /// <summary>The IdP is having trouble, not saying no: carry on, ask again next request.</summary>
    KeepForNow,
}

/// <summary>
/// The decision half of the role re-check — the same table every app on the central IdP follows
/// (central-login docs/integration-guide.md §4). Pure, so it is unit-tested without an IdP;
/// <see cref="RoleRecheckService"/> does the talking.
/// </summary>
public static class RoleRecheck
{
    /// <summary>Unix seconds of the last check the IdP answered, in the cookie's properties.</summary>
    public const string CheckedAtItem = ".roles_checked_at";

    public static bool IsDue(DateTimeOffset lastSuccess, DateTimeOffset now, TimeSpan every) =>
        now - lastSuccess >= every;

    /// <param name="hasRefreshToken">Without one there is nothing to ask the IdP with.</param>
    /// <param name="status">
    /// HTTP status of the refresh-token grant; null when there was no answer at all (network
    /// error, timeout, discovery unreachable).
    /// </param>
    /// <param name="roles">Roles in a 200's tokens; null when the 200 carried no readable token.</param>
    /// <param name="sinceLastSuccess">Time since the IdP last answered a check for this session.</param>
    /// <param name="maxOffline">How long an unreachable IdP is tolerated before failing closed.</param>
    public static RecheckVerdict Classify(
        bool hasRefreshToken,
        int? status,
        IReadOnlyCollection<string>? roles,
        TimeSpan sinceLastSuccess,
        TimeSpan maxOffline)
    {
        if (!hasRefreshToken)
        {
            return RecheckVerdict.SignOut;
        }

        // invalid_grant and friends: account disabled, token revoked or expired.
        if (status is >= 400 and < 500)
        {
            return RecheckVerdict.SignOut;
        }

        if (status is >= 200 and < 300 && roles is not null)
        {
            return roles.Any(OidcOptions.SignInRoles.Contains) ? RecheckVerdict.Renew : RecheckVerdict.SignOut;
        }

        // No answer, a 5xx, or a 200 with nothing readable in it. None of those is the IdP saying
        // "no", so an IdP restart does not sign everyone out — up to a point.
        return sinceLastSuccess > maxOffline ? RecheckVerdict.SignOut : RecheckVerdict.KeepForNow;
    }

    /// <summary>
    /// The <c>roles</c> claim of a JWT from the token endpoint, as a list (the IdP sends an
    /// array; a single string is accepted too). Null when the token is missing or unreadable.
    ///
    /// The signature is not checked: the token came straight from the IdP's token endpoint, over
    /// the app's own (TLS, client-authenticated) connection — OIDC Core 3.1.3.7 allows TLS server
    /// validation in place of the signature here. The id_token at sign-in was fully validated.
    /// </summary>
    public static IReadOnlyList<string>? ReadRoles(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        try
        {
            return new JsonWebToken(jwt).Claims
                                        .Where(claim => claim.Type == "roles")
                                        .Select(claim => claim.Value)
                                        .Distinct(StringComparer.Ordinal)
                                        .ToList();
        }
        catch (ArgumentException)
        {
            // Malformed (SecurityTokenMalformedException derives from ArgumentException).
            return null;
        }
    }
}
