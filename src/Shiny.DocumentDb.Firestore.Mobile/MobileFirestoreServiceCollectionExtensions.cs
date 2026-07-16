using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.DocumentDb.Firestore.Mobile;

namespace Shiny.DocumentDb;

public static class MobileFirestoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the on-device (native) Firebase Firestore <see cref="IDocumentStore"/> as a singleton, along
    /// with <see cref="IDocumentMaintenance"/> pointing at the same instance. iOS/Android only — the net10.0
    /// build resolves a stub that throws <see cref="PlatformNotSupportedException"/> on use.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the store options (ProjectId/AppId/ApiKey, persistence, collection/id/version mappings).</param>
    public static IServiceCollection AddMobileFirestoreDocumentStore(
        this IServiceCollection services,
        Action<MobileFirestoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MobileFirestoreOptions();
        configure(options);

        // Factory (not an eager instance) so the SP-taking ctor can wire DI-registered interceptors.
        services.AddSingleton<IDocumentStore>(sp => new MobileFirestoreDocumentStore(options, sp));
        services.TryAddSingleton<IDocumentMaintenance>(sp => (IDocumentMaintenance)sp.GetRequiredService<IDocumentStore>());
        services.TryAddSingleton<IObservableDocumentStore>(sp => (IObservableDocumentStore)sp.GetRequiredService<IDocumentStore>());
        services.TryAddSingleton<IChangeFeedDocumentStore>(sp => (IChangeFeedDocumentStore)sp.GetRequiredService<IDocumentStore>());
        return services;
    }

    /// <summary>
    /// Registers a managed <see cref="IFirebaseIdentity"/> (Firebase Auth REST) as a singleton — anonymous and
    /// email/password sign-in, current <c>uid</c>, and token refresh. Cross-platform (no native binding).
    /// </summary>
    public static IServiceCollection AddFirebaseIdentity(
        this IServiceCollection services,
        Action<FirebaseIdentityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FirebaseIdentityOptions();
        configure(options);
        services.AddSingleton(options);
        services.TryAddSingleton<IFirebaseIdentity>(sp => new FirebaseRestIdentity(sp.GetRequiredService<FirebaseIdentityOptions>()));
        return services;
    }
}
