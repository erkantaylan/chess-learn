namespace Repertoire.Api.Auth;

/// <summary>
/// The one and only local user. There is no registration and no users table; studies are owned by
/// this username (<see cref="StudyOwner"/>). Bound from the "Auth" configuration section, so
/// values arrive as Auth__Username / Auth__Password env vars (or user-secrets locally).
///
/// Or, when <see cref="OidcOptions.Authority"/> is set, sign-in goes to the central identity
/// provider instead and the password pair is ignored entirely. Each IdP account with the
/// <see cref="OidcOptions.RequiredRole"/> role has a library of its own, keyed by its <c>sub</c>;
/// a <see cref="OidcOptions.GuestRole"/> gets the in-memory board, signed in.
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

/// <summary>
/// Auth__Oidc__Authority / Auth__Oidc__ClientId / Auth__Oidc__ClientSecret, plus the re-check
/// timings Auth__Oidc__RecheckSeconds / Auth__Oidc__MaxOfflineSeconds.
/// </summary>
public sealed class OidcOptions
{
    /// <summary>The role, in the IdP's <c>roles</c> claim, that studies require.</summary>
    public const string RequiredRole = "user";

    /// <summary>
    /// The IdP gives this to every signed-in user. Enough to sign in and be named; not enough to
    /// read or write studies.
    /// </summary>
    public const string GuestRole = "guest";

    /// <summary>Holding either one lets you sign in, and keeps you signed in at each re-check.</summary>
    public static readonly IReadOnlySet<string> SignInRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        RequiredRole,
        GuestRole,
    };

    /// <summary>The issuer, e.g. https://id.example.com/application/o/chess-learn/.</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// How often a session's roles are re-read from the IdP (refresh-token grant), so a role
    /// removed or an account disabled there takes effect here within this long.
    /// </summary>
    public int RecheckSeconds { get; set; } = 300;

    /// <summary>
    /// While the IdP cannot be reached, sessions carry on with their last known roles — until
    /// their last successful check is older than this. Then they fail closed.
    /// </summary>
    public int MaxOfflineSeconds { get; set; } = 3600;
}
