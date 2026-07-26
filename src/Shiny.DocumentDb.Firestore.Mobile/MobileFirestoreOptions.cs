using System.Linq.Expressions;
using System.Text.Json;
using Shiny.DocumentDb.Internal;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Options for the on-device (native) Firebase Firestore document store. Each document type maps to its own
/// Firestore collection (the resolved type name, overridable via <see cref="MapTypeToCollection{T}"/>); the
/// document id is the document's string id. Persistence, the offline write queue, real-time listeners, and
/// conflict handling are provided by the native Firebase SDK — this options object configures the mapping
/// surface and how the native <c>FirebaseApp</c> is initialized.
/// </summary>
/// <remarks>
/// Deliberately does <b>not</b> reference <c>Google.Cloud.Firestore</c> (the admin SDK). This provider binds
/// the native mobile SDKs; see <c>plans/mobile-firestore-provider.md</c>.
/// </remarks>
public class MobileFirestoreOptions
{
    /// <summary>
    /// The shared per-type mapping state — id overrides and converters, query filters, and version mappings.
    /// Held (not inherited) so the fluent methods below keep returning <see cref="MobileFirestoreOptions"/>;
    /// <see cref="MobileFirestoreDocumentStore"/> hands it to <c>DocumentProviderBase</c>.
    /// </summary>
    internal DocumentMappingRegistry Mappings { get; } = new();

    readonly Dictionary<Type, string> collectionOverrides = new();

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

    /// <summary>
    /// When false, calling a reflection-based overload (without <c>JsonTypeInfo&lt;T&gt;</c>) throws if the
    /// type cannot be resolved from the configured TypeInfoResolver. Set false for iOS full-AOT. Default true.
    /// </summary>
    public bool UseReflectionFallback { get; set; } = true;

    /// <summary>Optional diagnostic callback.</summary>
    public Action<string>? Logging { get; set; }

    /// <summary>Overrides the Firestore collection (default the resolved type name) for a document type.</summary>
    public MobileFirestoreOptions MapTypeToCollection<T>(string collectionName) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        this.collectionOverrides[typeof(T)] = collectionName;
        return this;
    }

    internal string ResolveCollectionName(Type type, string typeName)
        => this.collectionOverrides.TryGetValue(type, out var mapped) ? mapped : typeName;

    /// <summary>Maps a document type to a custom Id property.</summary>
    public MobileFirestoreOptions MapIdProperty<T>(Expression<Func<T, object>> idProperty) where T : class
    {
        this.Mappings.MapIdProperty(idProperty);
        return this;
    }

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

    /// <summary>Registers an unnamed global query filter for <typeparamref name="T"/>.</summary>
    public MobileFirestoreOptions AddQueryFilter<T>(Expression<Func<T, bool>> predicate) where T : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return this.AddQueryFilterInternal<T>(null, predicate);
    }

    /// <summary>Registers a named global query filter for <typeparamref name="T"/>.</summary>
    public MobileFirestoreOptions AddQueryFilter<T>(string name, Expression<Func<T, bool>> predicate) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);
        return this.AddQueryFilterInternal<T>(name, predicate);
    }

    MobileFirestoreOptions AddQueryFilterInternal<T>(string? name, Expression<Func<T, bool>> predicate) where T : class
    {
        this.Mappings.AddQueryFilter(name, predicate);
        return this;
    }

    internal IReadOnlyList<QueryFilter> ResolveQueryFilters(Type type) => this.Mappings.ResolveQueryFilters(type);

    // ── Write interceptors ──────────────────────────────────────────────
    internal InterceptorPipeline Interceptors { get; } = new();

    /// <summary>Registers a per-document write interceptor. Registration order = execution order.</summary>
    public MobileFirestoreOptions AddInterceptor(IDocumentInterceptor interceptor) { this.Interceptors.Add(interceptor); return this; }

    /// <summary>Registers a set-based (bulk) write interceptor.</summary>
    public MobileFirestoreOptions AddBulkInterceptor(IDocumentBulkInterceptor interceptor) { this.Interceptors.AddBulk(interceptor); return this; }

    /// <summary>Registers a before-write callback scoped to documents of type <typeparamref name="T"/>.</summary>
    public MobileFirestoreOptions OnBeforeWrite<T>(Func<DocumentWriteContext, CancellationToken, Task> handler) where T : class { this.Interceptors.AddBefore<T>(handler); return this; }

    /// <summary>Registers an after-write callback scoped to documents of type <typeparamref name="T"/>.</summary>
    public MobileFirestoreOptions OnAfterWrite<T>(Func<DocumentWriteContext, CancellationToken, Task> handler) where T : class { this.Interceptors.AddAfter<T>(handler); return this; }

    /// <summary>
    /// Maps an <c>int</c> version property for optimistic concurrency. On device the adapter reads the current
    /// document inside a native Firestore transaction, compares the mapped version, increments, and writes — a
    /// stale version throws <see cref="ConcurrencyException"/>.
    /// </summary>
    public MobileFirestoreOptions MapVersionProperty<T>(Expression<Func<T, int>> property) where T : class
    {
        this.Mappings.MapVersionProperty(property);
        return this;
    }

    /// <summary>AOT-safe version-property overload.</summary>
    public MobileFirestoreOptions MapVersionProperty<T>(string propertyName, Func<T, int> getter, Action<T, int> setter) where T : class
    {
        this.Mappings.MapVersionProperty(propertyName, getter, setter);
        return this;
    }

    internal VersionMapping? ResolveVersionMapping(Type type) => this.Mappings.ResolveVersionMapping(type);
}
