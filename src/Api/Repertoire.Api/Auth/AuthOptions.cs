namespace Repertoire.Api.Auth;

/// <summary>
/// The one and only user. There is no registration, no users table and no user_id on studies —
/// the whole database belongs to this account. Bound from the "Auth" configuration section, so
/// values arrive as Auth__Username / Auth__Password env vars (or user-secrets locally).
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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
