using System.Security.Claims;

namespace Repertoire.Api.Auth;

/// <summary>
/// Who a study belongs to, read off the signed-in principal — the one place both sign-in modes
/// meet, so nothing else has to know which of them issued the cookie.
///
/// Central login: the IdP's <c>sub</c>. Never <c>preferred_username</c>, which the user can change
/// at the IdP and which would hand their library to whoever takes the old name next.
/// Password login: the configured Auth:Username, which /api/login puts in the cookie as the
/// standard name claim (<see cref="ClaimTypes.Name"/>) and nothing else.
///
/// The two cannot be confused: with MapInboundClaims off the OIDC cookie names its user
/// <c>preferred_username</c>, not <see cref="ClaimTypes.Name"/>, so a central session without a
/// <c>sub</c> has no owner at all rather than falling back to a mutable display name.
/// </summary>
public static class StudyOwner
{
    /// <summary>The OIDC subject claim, exactly as the IdP names it (MapInboundClaims is off).</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The owner id, or null when the principal is signed out or carries neither claim.</summary>
    public static string? Find(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? owner = user.FindFirst(SubjectClaim)?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(owner) ? null : owner;
    }

    /// <summary>
    /// For endpoints behind RequireAuthorization, where a principal is always one of the two
    /// shapes above. Throws rather than returning a guessable default: a study saved under "" or
    /// "unknown" would be shared by every session that ever hit the same bug.
    /// </summary>
    public static string RequireStudyOwner(this ClaimsPrincipal user) =>
        Find(user) ?? throw new InvalidOperationException(
            $"Signed-in principal has neither a '{SubjectClaim}' nor a '{ClaimTypes.Name}' claim, so it owns no studies.");
}
