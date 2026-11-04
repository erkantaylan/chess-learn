using Microsoft.AspNetCore.DataProtection;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Auth;

public static class DataProtectionExtensions
{
    /// <summary>
    /// The name every key is bound to. Fixed, because the default is derived from the content root
    /// path — a different path in a rebuilt image would make every existing key unreadable.
    /// </summary>
    public const string ApplicationName = "repertoire";

    /// <summary>
    /// Keeps the Data Protection key ring in Postgres (<c>data_protection_keys</c>) instead of the
    /// container's home directory. Without this a redeploy throws the keys away, and with them every
    /// <c>repertoire.auth</c> cookie and any OIDC sign-in that was mid-flight. Both auth modes use it:
    /// the password login and the central login issue the same cookie.
    /// </summary>
    public static IServiceCollection AddRepertoireDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
                .SetApplicationName(ApplicationName)
                .PersistKeysToDbContext<StudyDbContext>();
        return services;
    }
}
