using Projects;

namespace Repertoire.AppHost;

internal static class AspireProgram
{
    public static async Task Main(string[] args)
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

        var database = builder
                       .AddPostgres("db-repertoire")
                       // Two different things, both needed, and neither is sufficient alone:
                       //
                       // Persistent lifetime keeps the CONTAINER across AppHost restarts — but only
                       // while its config hash is unchanged. Change anything about the resource
                       // (upgrade Aspire, add or drop a sibling like pgAdmin) and Aspire recreates
                       // it. With the default anonymous volume that silently strands the data in an
                       // orphaned volume, which is exactly how the first Urusov study was lost.
                       //
                       // The named data volume is what actually makes the DATA durable: recreate the
                       // container as often as you like, it remounts the same volume. Requires the
                       // UserSecretsId in this csproj too — a regenerated password would not match
                       // the credentials already initialised inside the volume.
                       .WithLifetime(ContainerLifetime.Persistent)
                       .WithDataVolume("repertoire-pgdata")
                       .PublishAsContainer()
                       .AddDatabase("cs-repertoire", "repertoire");

        builder.AddProject<Repertoire_Api>("api")
               // No fixed port: Aspire assigns it, so two stacks can run side by side and nothing
               // collides with the Python server still sitting on 8000. Entry point is on the
               // dashboard.
               .WithHttpEndpoint()
               .WithReference(database)
               .WaitFor(database);

        await builder.Build().RunAsync();
    }
}
