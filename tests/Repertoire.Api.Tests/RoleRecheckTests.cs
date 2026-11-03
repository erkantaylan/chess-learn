using System.Text;
using Repertoire.Api.Auth;

namespace Repertoire.Api.Tests;

/// <summary>The integration guide's §4 table, row by row.</summary>
public class RoleRecheckTests
{
    private static readonly TimeSpan MaxOffline = TimeSpan.FromHours(1);
    private static readonly TimeSpan Recently = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan LongAgo = TimeSpan.FromMinutes(61);

    [Theory]
    [InlineData("user,guest")]
    [InlineData("guest")]
    [InlineData("user")]
    public void A_200_with_a_role_for_this_app_renews(string roles) =>
        Assert.Equal(RecheckVerdict.Renew, RoleRecheck.Classify(true, 200, Split(roles), Recently, MaxOffline));

    [Theory]
    [InlineData("")]
    [InlineData("admin,User")] // ordinal: "User" is not "user"
    public void A_200_with_no_role_for_this_app_signs_out(string roles) =>
        Assert.Equal(RecheckVerdict.SignOut, RoleRecheck.Classify(true, 200, Split(roles), Recently, MaxOffline));

    [Theory]
    [InlineData(400)] // invalid_grant: disabled, revoked, expired
    [InlineData(401)]
    [InlineData(403)]
    public void A_4xx_signs_out_even_right_after_a_good_check(int status) =>
        Assert.Equal(RecheckVerdict.SignOut, RoleRecheck.Classify(true, status, null, TimeSpan.Zero, MaxOffline));

    [Fact]
    public void No_refresh_token_signs_out() =>
        Assert.Equal(RecheckVerdict.SignOut, RoleRecheck.Classify(false, null, null, Recently, MaxOffline));

    [Theory]
    [InlineData(null)] // network error / timeout: the IdP is unreachable
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void An_unreachable_or_failing_IdP_keeps_the_session_for_a_while(int? status) =>
        Assert.Equal(RecheckVerdict.KeepForNow, RoleRecheck.Classify(true, status, null, Recently, MaxOffline));

    [Theory]
    [InlineData(null)]
    [InlineData(503)]
    public void An_unreachable_IdP_fails_closed_after_max_offline(int? status) =>
        Assert.Equal(RecheckVerdict.SignOut, RoleRecheck.Classify(true, status, null, LongAgo, MaxOffline));

    [Fact]
    public void A_200_without_a_readable_token_counts_as_a_failing_IdP()
    {
        Assert.Equal(RecheckVerdict.KeepForNow, RoleRecheck.Classify(true, 200, null, Recently, MaxOffline));
        Assert.Equal(RecheckVerdict.SignOut, RoleRecheck.Classify(true, 200, null, LongAgo, MaxOffline));
    }

    [Fact]
    public void Due_once_the_interval_has_passed()
    {
        DateTimeOffset checkedAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        TimeSpan every = TimeSpan.FromSeconds(300);
        Assert.False(RoleRecheck.IsDue(checkedAt, checkedAt.AddSeconds(299), every));
        Assert.True(RoleRecheck.IsDue(checkedAt, checkedAt.AddSeconds(300), every));
    }

    [Fact]
    public void Reads_roles_from_an_array_claim() =>
        Assert.Equal(["user", "guest"], RoleRecheck.ReadRoles(Jwt("""{"sub":"1","roles":["user","guest"]}""")));

    [Fact]
    public void Reads_a_single_string_role() =>
        Assert.Equal(["guest"], RoleRecheck.ReadRoles(Jwt("""{"sub":"1","roles":"guest"}""")));

    [Fact]
    public void A_token_without_roles_has_none() =>
        Assert.Empty(RoleRecheck.ReadRoles(Jwt("""{"sub":"1"}"""))!);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public void A_missing_or_malformed_token_is_unreadable(string? token) =>
        Assert.Null(RoleRecheck.ReadRoles(token));

    private static string[] Split(string roles) => roles.Split(',', StringSplitOptions.RemoveEmptyEntries);

    internal static string Jwt(string payload) =>
        $"{Base64Url("""{"alg":"RS256","typ":"JWT"}""")}.{Base64Url(payload)}.c2ln";

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
