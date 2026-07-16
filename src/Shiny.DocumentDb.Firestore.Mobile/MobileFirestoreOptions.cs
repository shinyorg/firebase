using System.Diagnostics.CodeAnalysis;
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
    readonly Dictionary<Type, string> collectionOverrides = new();
    readonly Dictionary<Type, string> idPropertyOverrides = new();
    readonly IdConverterRegistry idConverters = new();
    readonly Dictionary<Type, List<QueryFilter>> queryFilters = new();
    internal readonly Dictionary<Type, VersionMapping> versionMappings = new();

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
        this.idPropertyOverrides[typeof(T)] = ExtractPropertyName(idProperty);
        return this;
    }

    internal string? ResolveIdPropertyName(Type type)
        => this.idPropertyOverrides.TryGetValue(type, out var name) ? name : null;

    /// <summary>Registers a converter so a document Id can be a CLR type beyond Guid/int/long/string.</summary>
    public MobileFirestoreOptions MapIdType<TId>(DocumentIdConverter<TId> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        this.idConverters.Register(converter);
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
        this.idConverters.Register(new DelegateIdConverter<TId>(toString, parse, isDefault, generate));
        return this;
    }

    internal IdConverterRegistry IdConverters => this.idConverters;

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
        if (!this.queryFilters.TryGetValue(typeof(T), out var list))
            this.queryFilters[typeof(T)] = list = new List<QueryFilter>();
        list.Add(new QueryFilter(name, predicate));
        return this;
    }

    internal IReadOnlyList<QueryFilter> ResolveQueryFilters(Type type)
        => this.queryFilters.TryGetValue(type, out var list) ? list : Array.Empty<QueryFilter>();

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
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Property is resolved by name from a user-provided expression.")]
    public MobileFirestoreOptions MapVersionProperty<T>(Expression<Func<T, int>> property) where T : class
    {
        if (property.Body is not MemberExpression member)
            throw new ArgumentException("Expression must be a simple property access.", nameof(property));

        var propertyName = member.Member.Name;
        var propInfo = typeof(T).GetProperty(propertyName)
            ?? throw new ArgumentException($"Property '{propertyName}' not found on type '{typeof(T).Name}'.");

        this.versionMappings[typeof(T)] = new VersionMapping
        {
            DocumentType = typeof(T),
            PropertyName = propertyName,
            GetVersion = obj => (int)propInfo.GetValue(obj)!,
            SetVersion = (obj, v) => propInfo.SetValue(obj, v)
        };
        return this;
    }

    /// <summary>AOT-safe version-property overload.</summary>
    public MobileFirestoreOptions MapVersionProperty<T>(string propertyName, Func<T, int> getter, Action<T, int> setter) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        this.versionMappings[typeof(T)] = new VersionMapping
        {
            DocumentType = typeof(T),
            PropertyName = propertyName,
            GetVersion = obj => getter((T)obj),
            SetVersion = (obj, v) => setter((T)obj, v)
        };
        return this;
    }

    internal VersionMapping? ResolveVersionMapping(Type type)
        => this.versionMappings.TryGetValue(type, out var mapping) ? mapping : null;

    static string ExtractPropertyName<T>(Expression<Func<T, object>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is MemberExpression member)
            return member.Member.Name;

        throw new ArgumentException(
            "Expression must be a simple property access (e.g., x => x.MyId).", nameof(expression));
    }
}
