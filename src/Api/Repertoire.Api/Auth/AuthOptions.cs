namespace Repertoire.Api.Auth;

/// <summary>
/// The one and only user. There is no registration, no users table and no user_id on studies —
/// the whole database belongs to this account. Bound from the "Auth" configuration section, so
/// values arrive as Auth__Username / Auth__Password env vars (or user-secrets locally).
///
/// Or, when <see cref="OidcOptions.Authority"/> is set, sign-in goes to the central identity
/// provider instead and the password pair is ignored entirely. Still one library: anyone the IdP
/// lets in with the <see cref="OidcOptions.RequiredRole"/> role shares it.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Plaintext, compared in fixed time. For one static account behind an HttpOnly cookie a
    /// password hash would only protect against someone who can already read your environment —
    /// at which point they have the database too. Keep it out of the repo, not out of memory.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    public OidcOptions Oidc { get; set; } = new();

    /// <summary>The switch: an authority means OIDC is the only way in.</summary>
    public bool UsesOidc => !string.IsNullOrWhiteSpace(Oidc.Authority);

    public bool IsConfigured => UsesOidc
        ? !string.IsNullOrWhiteSpace(Oidc.ClientId) && !string.IsNullOrWhiteSpace(Oidc.ClientSecret)
        : !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Auth__Oidc__Authority / Auth__Oidc__ClientId / Auth__Oidc__ClientSecret.</summary>
public sealed class OidcOptions
{
    /// <summary>The role, in the IdP's <c>roles</c> claim, that this app requires.</summary>
    public const string RequiredRole = "user";

    /// <summary>The issuer, e.g. https://id.example.com/application/o/chess-learn/.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
