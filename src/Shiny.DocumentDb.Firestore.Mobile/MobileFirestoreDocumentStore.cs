using System.Text.Json.Serialization.Metadata;
using Shiny.DocumentDb.Internal;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// On-device Firebase Firestore document store. The native Firebase SDK owns persistence, the offline write
/// queue, real-time listeners, and conflict handling; this class is the thin adapter that maps
/// <see cref="IDocumentStore"/> operations onto native Firestore calls and translates documents to/from the
/// native field map.
/// </summary>
/// <remarks>
/// The real implementation lives in the platform partial (<c>MobileFirestoreDocumentStore.Android.cs</c>,
/// compiled for <c>net10.0-android</c>). On the plain <c>net10.0</c> build the stub partial
/// (<c>MobileFirestoreDocumentStore.Stub.cs</c>) throws <see cref="PlatformNotSupportedException"/>.
/// </remarks>
public partial class MobileFirestoreDocumentStore : DocumentProviderBase, IDocumentStore, IDocumentMaintenance, IObservableDocumentStore, IChangeFeedDocumentStore
{
    readonly MobileFirestoreOptions options;
    readonly IServiceProvider services;
    readonly IdAccessorCache idCache;

    public MobileFirestoreDocumentStore(MobileFirestoreOptions options, IServiceProvider services)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.services = services;
        DocumentConfigurationValidator.Validate(options);
        this.idCache = new IdAccessorCache(options.ResolveIdPropertyName, options.IdConverters);
        this.InitializePlatform();
    }

    protected override InterceptorPipeline Interceptors => this.options.Interceptors;
    protected override DocumentMappingRegistry Mappings => this.options.Mappings;
    protected override IdAccessorCache IdCache => this.idCache;
    protected override JsonTypeInfo<T>? ResolveTypeInfo<T>(JsonTypeInfo<T>? provided) where T : class => this.FindTypeInfo(provided);
    protected override string ResolveDocumentTypeName<T>() where T : class => ResolveTypeName<T>(this.options.TypeNameResolution);

    // Implemented per-platform: Android configures the native FirebaseFirestore instance; the stub no-ops.
    partial void InitializePlatform();

    // Resolve the Firestore collection name for a document type (respects cfg.ToCollection / cfg.Table).
    string ResolveCollection<T>() where T : class => this.options.ResolveCollectionName(typeof(T), this.ResolveDocumentTypeName<T>());

    // TypeNameResolver is internal to Shiny.DocumentDb, so mirror it here.
    static string ResolveTypeName<T>(TypeNameResolution resolution) => resolution switch
    {
        TypeNameResolution.ShortName => typeof(T).Name,
        TypeNameResolution.FullName => typeof(T).FullName ?? typeof(T).Name,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution))
    };

    // The effective JsonTypeInfo<T>: the caller's, the store's resolver, or null for the reflection path.
    internal JsonTypeInfo<T>? FindTypeInfo<T>(JsonTypeInfo<T>? provided)
    {
        if (provided != null)
            return provided;

        var json = this.options.JsonSerializerOptions;
        if (json != null && json.TryGetTypeInfo(typeof(T), out var info) && info is JsonTypeInfo<T> typed)
            return typed;

        if (!this.options.UseReflectionFallback)
            throw new InvalidOperationException(
                $"No JsonTypeInfo registered for type '{typeof(T).FullName}'. " +
                $"Register it in your JsonSerializerContext or pass a JsonTypeInfo<{typeof(T).Name}> explicitly.");

        return null;
    }
}
