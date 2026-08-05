using System.Text.Json;
using Shiny.DocumentDb.Internal;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Options for the on-device (native) Firebase Firestore document store. Each document type maps to its own
/// Firestore collection (the resolved type name, overridable with <c>cfg.ToCollection(…)</c> inside a
/// <see cref="ConfigureDocumentExtensions.ConfigureDocument{T}"/> block); the document id is the document's
/// string id. Persistence, the offline write queue, real-time listeners, and conflict handling are provided by
/// the native Firebase SDK — this options object configures the mapping surface and how the native
/// <c>FirebaseApp</c> is initialized.
/// </summary>
/// <remarks>
/// Deliberately does <b>not</b> reference <c>Google.Cloud.Firestore</c> (the admin SDK). This provider binds
/// the native mobile SDKs; see <c>plans/mobile-firestore-provider.md</c>.
/// </remarks>
public class MobileFirestoreOptions : IDocumentStoreOptions
{
    /// <summary>
    /// The shared per-type mapping state — id overrides and converters, query filters, version mappings and
    /// write interceptors. Held (not inherited) so the store-level methods below keep returning
    /// <see cref="MobileFirestoreOptions"/>; <see cref="MobileFirestoreDocumentStore"/> hands it to
    /// <c>DocumentProviderBase</c>, and <c>ConfigureDocument&lt;T&gt;</c> writes into it.
    /// </summary>
    internal DocumentMappingRegistry Mappings { get; } = new();

    /// <summary>
    /// The Firebase project id. Optional when a platform config file (<c>google-services.json</c> on Android,
    /// <c>GoogleService-Info.plist</c> on iOS) is bundled — the native SDK reads it from there. When set, it
    /// takes precedence for explicit <c>FirebaseApp</c> initialization.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>The Firebase application id (e.g. <c>1:1234567890:ios:abcdef</c>). Paired with <see cref="ProjectId"/> for explicit init.</summary>
    public string? AppId { get; set; }

    /// <summary>The Firebase Web API key used by Firebase Auth. Optional when read from the platform config file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Enables the native Firestore persistent (offline) cache. Default true — this is the whole point of the
    /// mobile provider: reads are cache-first and writes queue offline, draining automatically on reconnect.
    /// </summary>
    public bool PersistenceEnabled { get; set; } = true;

    /// <summary>
    /// Optional <c>host:port</c> of the Firestore emulator. When set the native SDK is pointed at the emulator
    /// (<c>useEmulator</c>) instead of production — used by the device/emulator integration tests.
    /// </summary>
    public string? EmulatorHost { get; set; }

    public TypeNameResolution TypeNameResolution { get; set; } = TypeNameResolution.ShortName;
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    // The provider-agnostic view of the serializer options, so a cross-cutting feature (field-level encryption)
    // can attach a JsonTypeInfo modifier without knowing which options class it holds.
    JsonSerializerOptions? IDocumentStoreOptions.SerializerOptions
    {
        get => this.JsonSerializerOptions;
        set => this.JsonSerializerOptions = value;
    }

    /// <summary>
    /// When false, calling a reflection-based overload (without <c>JsonTypeInfo&lt;T&gt;</c>) throws if the
    /// type cannot be resolved from the configured TypeInfoResolver. Set false for iOS full-AOT. Default true.
    /// </summary>
    public bool UseReflectionFallback { get; set; } = true;

    /// <summary>Optional diagnostic callback.</summary>
    public Action<string>? Logging { get; set; }

    internal string ResolveCollectionName(Type type, string typeName)
        => this.Mappings.ResolveMappedName(typeName, typeName);

    internal string? ResolveIdPropertyName(Type type) => this.Mappings.ResolveIdPropertyName(type);

    /// <summary>Registers a converter so a document Id can be a CLR type beyond Guid/int/long/string.</summary>
    public MobileFirestoreOptions MapIdType<TId>(DocumentIdConverter<TId> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        this.Mappings.IdConverters.Register(converter);
        return this;
    }

    /// <summary>Registers a custom Id type using inline delegates.</summary>
    public MobileFirestoreOptions MapIdType<TId>(
        Func<TId, string> toString,
        Func<string, TId> parse,
        Func<TId, bool>? isDefault = null,
        Func<TId>? generate = null)
    {
        ArgumentNullException.ThrowIfNull(toString);
        ArgumentNullException.ThrowIfNull(parse);
        this.Mappings.IdConverters.Register(new DelegateIdConverter<TId>(toString, parse, isDefault, generate));
        return this;
    }

    internal IdConverterRegistry IdConverters => this.Mappings.IdConverters;

    internal IReadOnlyList<QueryFilter> ResolveQueryFilters(Type type) => this.Mappings.ResolveQueryFilters(type);

    // ── Write interceptors ──────────────────────────────────────────────
    internal InterceptorPipeline Interceptors => this.Mappings.Interceptors;

    /// <summary>Registers a per-document write interceptor. Registration order = execution order.</summary>
    public MobileFirestoreOptions AddInterceptor(IDocumentInterceptor interceptor) { this.Interceptors.Add(interceptor); return this; }

    /// <summary>Registers a set-based (bulk) write interceptor.</summary>
    public MobileFirestoreOptions AddBulkInterceptor(IDocumentBulkInterceptor interceptor) { this.Interceptors.AddBulk(interceptor); return this; }

    internal VersionMapping? ResolveVersionMapping(Type type) => this.Mappings.ResolveVersionMapping(type);

    // ── IDocumentStoreOptions ───────────────────────────────────────────

    /// <summary>
    /// What the native Firestore SDK can do on device. The mobile SDK has no spatial or vector engine, no
    /// full-text index, and this provider keeps no temporal, blob or computed sidecars — a Firestore write is
    /// one document and nothing else, which is what makes the offline queue hold. Read by
    /// <see cref="DocumentConfigurationValidator"/> when the store is built.
    /// </summary>
    DocumentStoreCapabilities IDocumentStoreOptions.Capabilities => new()
    {
        ProviderName = "Firestore (mobile)",
        PerTypeStorageName = true,
        Spatial = false,
        Vector = false,
        FullText = false,
        Temporal = false,
        Blobs = false,
        ComputedProperties = false
    };

    DocumentMappingRegistry IDocumentStoreOptions.Mappings => this.Mappings;

    IDocumentStoreOptions IDocumentStoreOptions.AddInterceptor(IDocumentInterceptor interceptor)
        => this.AddInterceptor(interceptor);

    IDocumentStoreOptions IDocumentStoreOptions.AddBulkInterceptor(IDocumentBulkInterceptor interceptor)
        => this.AddBulkInterceptor(interceptor);
}
