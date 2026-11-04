using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Repertoire.Api.Auth;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Tests;

/// <summary>The key ring must outlive the container: in Postgres, under a name that does not depend on paths.</summary>
public class DataProtectionTests
{
    // Never connected to: neither the options nor the model comparison below opens a connection.
    private const string Unreachable = "Host=127.0.0.1;Port=1;Username=x;Password=x;Database=x";

    private static ServiceProvider Build()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<StudyDbContext>(o => o.UseNpgsql(Unreachable));
        services.AddRepertoireDataProtection();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Keys_are_stored_through_the_study_database()
    {
        using ServiceProvider provider = Build();
        KeyManagementOptions options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<EntityFrameworkCoreXmlRepository<StudyDbContext>>(options.XmlRepository);
    }

    [Fact]
    public void The_application_name_is_fixed()
    {
        using ServiceProvider provider = Build();
        DataProtectionOptions options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal("repertoire", options.ApplicationDiscriminator);
    }

    [Fact]
    public void The_migrations_cover_the_model()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();
        StudyDbContext db = scope.ServiceProvider.GetRequiredService<StudyDbContext>();
        Assert.False(db.Database.HasPendingModelChanges(), "Model changed without a migration: dotnet ef migrations add <Name>");
    }
}
