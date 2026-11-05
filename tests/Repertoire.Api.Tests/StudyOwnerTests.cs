using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Repertoire.Api.Auth;

namespace Repertoire.Api.Tests;

/// <summary>Both sign-in modes' principals, as their cookies actually carry them.</summary>
public class StudyOwnerTests
{
    /// <summary>What the OIDC handler builds: MapInboundClaims off, name and role claim types as the IdP's.</summary>
    internal static ClaimsPrincipal Central(string? sub, string username = "erkan")
    {
        List<Claim> claims = [new("preferred_username", username), new("roles", OidcOptions.RequiredRole)];
        if (sub is not null)
        {
            claims.Add(new Claim(StudyOwner.SubjectClaim, sub));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "AuthenticationTypes.Federation", "preferred_username", "roles"));
    }

    /// <summary>Exactly what POST /api/login signs in with.</summary>
    internal static ClaimsPrincipal Password(string username) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], CookieAuthenticationDefaults.AuthenticationScheme));

    [Fact]
    public void Central_login_owner_is_the_subject() =>
        Assert.Equal("8f3a-sub", StudyOwner.Find(Central("8f3a-sub")));

    [Fact]
    public void Central_login_without_a_subject_has_no_owner_rather_than_its_username()
    {
        ClaimsPrincipal user = Central(sub: null, username: "erkan");

        // Identity.Name is "erkan" here — exactly the mutable value the owner must never be.
        Assert.Equal("erkan", user.Identity?.Name);
        Assert.Null(StudyOwner.Find(user));
        Assert.Throws<InvalidOperationException>(() => user.RequireStudyOwner());
    }

    [Fact]
    public void Password_login_owner_is_the_configured_username() =>
        Assert.Equal("erkan", Password("erkan").RequireStudyOwner());

    [Fact]
    public void Signed_out_has_no_owner()
    {
        Assert.Null(StudyOwner.Find(new ClaimsPrincipal(new ClaimsIdentity())));

        // Claims without an authentication type are not a session, whatever they say.
        Assert.Null(StudyOwner.Find(new ClaimsPrincipal(new ClaimsIdentity([new Claim(StudyOwner.SubjectClaim, "x")]))));
    }
}
