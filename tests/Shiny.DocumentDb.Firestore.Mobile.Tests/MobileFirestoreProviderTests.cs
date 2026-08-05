using Microsoft.Extensions.DependencyInjection;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Firestore.Mobile;
using Xunit;

namespace Shiny.DocumentDb.Tests.MobileFirestore;

// Managed-surface tests for the mobile Firestore provider. These exercise the parts that are device-free:
// DI wiring, the options/mapping API, and the net10.0 stub contract. The on-device adapter (CRUD, query,
// offline, real-time) is covered by the instrumented device/emulator test app, not here.
public class MobileFirestoreProviderTests
{
    class Play
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public int Version { get; set; }
    }

    static IServiceProvider Build(Action<MobileFirestoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddMobileFirestoreDocumentStore(o =>
        {
            o.ProjectId = "test-project";
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Di_registers_store_and_maintenance_as_same_singleton()
    {
        var sp = Build();
        var store = sp.GetRequiredService<IDocumentStore>();
        var maintenance = sp.GetRequiredService<IDocumentMaintenance>();

        Assert.NotNull(store);
        Assert.IsType<MobileFirestoreDocumentStore>(store);
        Assert.Same(store, maintenance);
    }

    [Fact]
    public void ConfigureDocument_maps_collection_id_and_version()
    {
        var options = new MobileFirestoreOptions();
        var returned = options.ConfigureDocument<Play>(cfg => cfg
            .ToCollection("plays")
            .MapVersionProperty(x => x.Version)
            .MapIdProperty(x => x.Id)
        );

        Assert.Same(options, returned);
        Assert.Equal("plays", options.ResolveCollectionName(typeof(Play), nameof(Play)));
        Assert.Equal(nameof(Play.Id), options.ResolveIdPropertyName(typeof(Play)));
        Assert.NotNull(options.ResolveVersionMapping(typeof(Play)));
        Assert.True(options.PersistenceEnabled); // offline cache on by default
    }

    [Fact]
    public void ConfigureDocument_defaults_collection_to_the_type_name()
    {
        var options = new MobileFirestoreOptions();
        Assert.Equal(nameof(Play), options.ResolveCollectionName(typeof(Play), nameof(Play)));
    }

    [Fact]
    public void Store_rejects_a_mapping_the_native_sdk_cannot_honor()
    {
        var services = new ServiceCollection();
        services.AddMobileFirestoreDocumentStore(o =>
        {
            o.ProjectId = "test-project";
            o.ConfigureDocument<Play>(cfg => cfg.MapTemporal());
        });

        var ex = Assert.Throws<DocumentConfigurationException>(
            () => services.BuildServiceProvider().GetRequiredService<IDocumentStore>()
        );
        Assert.Contains("Firestore (mobile)", ex.Message);
    }

    [Fact]
    public async Task Stub_throws_platform_not_supported_on_use()
    {
        var store = Build().GetRequiredService<IDocumentStore>();

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => store.Get<Play>("abc")
        );
        Assert.Throws<PlatformNotSupportedException>(
            () => store.Query<Play>()
        );
    }

    [Fact]
    public void Identity_di_registers_rest_service()
    {
        var services = new ServiceCollection();
        services.AddFirebaseIdentity(o => { o.ApiKey = "k"; });
        var identity = services.BuildServiceProvider().GetRequiredService<IFirebaseIdentity>();

        Assert.IsType<FirebaseRestIdentity>(identity);
        Assert.Null(identity.CurrentUser);
        Assert.Null(identity.CurrentUserId);
    }

    [Fact]
    public void Identity_requires_api_key()
        => Assert.Throws<ArgumentException>(() => new FirebaseRestIdentity(new FirebaseIdentityOptions()));

    [Fact]
    public async Task Identity_get_token_without_signin_throws()
    {
        var identity = new FirebaseRestIdentity(new FirebaseIdentityOptions { ApiKey = "k" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => identity.GetIdTokenAsync());
    }
}
