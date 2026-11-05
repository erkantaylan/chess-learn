using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Tests;

/// <summary>
/// Studies are per-user: you list and write your own, and read anyone's by id. Every test signs
/// in as fresh subjects, so they share one database without seeing each other's rows.
/// </summary>
[Collection(StudiesApiCollection.Name)]
public class StudyOwnershipTests(StudiesApiFactory app)
{
    private static string NewSub() => $"sub-{Guid.NewGuid():N}";

    private static async Task<JsonElement> Create(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/studies", new
        {
            name,
            tree = new { san = "", children = new[] { new { san = "e4", children = Array.Empty<object>() } } },
            pgn = "1. e4 *",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;

    private async Task<Study?> Stored(int id)
    {
        using IServiceScope scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<StudyDbContext>()
                          .Studies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    [Fact]
    public async Task Listing_shows_only_your_own_studies()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        using HttpClient bob = app.AsCentral(NewSub());

        await Create(alice, "Alice's Italian");
        await Create(alice, "Alice's French");
        await Create(bob, "Bob's Sicilian");

        JsonElement[] list = await alice.GetFromJsonAsync<JsonElement[]>("/api/studies") ?? [];

        Assert.Equal(["Alice's French", "Alice's Italian"], list.Select(row => row.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Creating_records_the_caller_as_owner()
    {
        string sub = NewSub();
        using HttpClient alice = app.AsCentral(sub);

        JsonElement created = await Create(alice, "Mine");

        Assert.True(created.GetProperty("owned").GetBoolean());
        Assert.Equal(sub, (await Stored(created.GetProperty("id").GetInt32()))!.Owner);
        Assert.False(created.TryGetProperty("owner", out _), "the owner's subject id must not go out on the wire");
    }

    [Fact]
    public async Task Anyone_who_may_read_studies_can_open_any_study_by_id()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        using HttpClient bob = app.AsCentral(NewSub());
        int id = (await Create(alice, "Shared link")).GetProperty("id").GetInt32();

        JsonElement asOwner = await alice.GetFromJsonAsync<JsonElement>($"/api/studies/{id}");
        JsonElement asOther = await bob.GetFromJsonAsync<JsonElement>($"/api/studies/{id}");

        Assert.True(asOwner.GetProperty("owned").GetBoolean());
        Assert.False(asOther.GetProperty("owned").GetBoolean());
        Assert.Equal("Shared link", asOther.GetProperty("name").GetString());
        Assert.Equal("e4", asOther.GetProperty("tree").GetProperty("children")[0].GetProperty("san").GetString());
    }

    [Fact]
    public async Task Saving_over_someone_elses_study_is_404_not_403_and_changes_nothing()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        using HttpClient bob = app.AsCentral(NewSub());
        int id = (await Create(alice, "Alice's")).GetProperty("id").GetInt32();

        HttpResponseMessage response = await bob.PutAsJsonAsync($"/api/studies/{id}", new { name = "Bob was here" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"study {id} is not one of your studies", await Detail(response));
        Assert.Equal("Alice's", (await Stored(id))!.Name);
    }

    [Fact]
    public async Task Deleting_someone_elses_study_is_404_and_keeps_it()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        using HttpClient bob = app.AsCentral(NewSub());
        int id = (await Create(alice, "Alice's")).GetProperty("id").GetInt32();

        HttpResponseMessage response = await bob.DeleteAsync($"/api/studies/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"study {id} is not one of your studies", await Detail(response));
        Assert.NotNull(await Stored(id));
    }

    [Fact]
    public async Task The_owner_can_save_and_delete()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        int id = (await Create(alice, "Draft")).GetProperty("id").GetInt32();

        HttpResponseMessage renamed = await alice.PutAsJsonAsync($"/api/studies/{id}", new { name = "Final" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.True((await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("owned").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/studies/{id}")).StatusCode);
        Assert.Null(await Stored(id));
    }

    [Fact]
    public async Task An_unknown_id_is_still_plain_not_found()
    {
        using HttpClient alice = app.AsCentral(NewSub());
        const int missing = int.MaxValue;

        HttpResponseMessage get = await alice.GetAsync($"/api/studies/{missing}");
        HttpResponseMessage put = await alice.PutAsJsonAsync($"/api/studies/{missing}", new { name = "x" });
        HttpResponseMessage delete = await alice.DeleteAsync($"/api/studies/{missing}");

        foreach (HttpResponseMessage response in new[] { get, put, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal($"study {missing} not found", await Detail(response));
        }
    }

    [Fact]
    public async Task Password_and_central_sessions_with_the_same_display_name_are_different_owners()
    {
        // The central principals here are all named "same-display-name" (StudiesApiFactory), so a
        // sub-less fallback to the name would put every one of them in a single library.
        using HttpClient central = app.AsCentral(NewSub());
        using HttpClient password = app.CreateClient();
        password.DefaultRequestHeaders.Add(StudiesApiFactory.PasswordUserHeader, "same-display-name");

        await Create(central, "Central's");

        JsonElement[] list = await password.GetFromJsonAsync<JsonElement[]>("/api/studies") ?? [];
        Assert.DoesNotContain(list, row => row.GetProperty("name").GetString() == "Central's");
    }

    [Fact]
    public async Task The_real_password_login_owns_studies_as_the_configured_username()
    {
        // No test header: the real POST /api/login and the repertoire.auth cookie it sets
        // (appsettings.Development.json's erkan/chess).
        using HttpClient client = app.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/login", new { username = "erkan", password = "chess" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        JsonElement created = await Create(client, "Through the real cookie");

        Assert.Equal("erkan", (await Stored(created.GetProperty("id").GetInt32()))!.Owner);
    }

    [Fact]
    public async Task Signed_out_gets_401_everywhere_under_studies()
    {
        using HttpClient anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/studies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/studies/1")).StatusCode);
    }
}
