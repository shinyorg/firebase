#if !ANDROID && !IOS
using System.Linq.Expressions;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.DocumentDb.Firestore.Mobile;

// net10.0 (non-mobile) stub: every operation throws. The real adapters are
// Platforms/Android/MobileFirestoreDocumentStore.Android.cs and Platforms/iOS/MobileFirestoreDocumentStore.iOS.cs.
public partial class MobileFirestoreDocumentStore
{
    partial void InitializePlatform() { }

    static PlatformNotSupportedException Unsupported() => new(
        "Shiny.DocumentDb.Firestore.Mobile runs only on Android and iOS. " +
        "This is the net10.0 stub build — reference the provider from a net10.0-android or net10.0-ios head."
    );

    public IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class => throw Unsupported();
    public Task<T?> Get<T>(object id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<JsonPatchDocument<T>?> GetDiff<T>(object id, T modified, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();

    public Task Insert<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<int> BatchInsert<T>(IEnumerable<T> documents, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task Update<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task Upsert<T>(T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<bool> SetProperty<T>(object id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<bool> RemoveProperty<T>(object id, Expression<Func<T, object>> property, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<bool> Remove<T>(object id, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class => throw Unsupported();

    public Task ClearAll(CancellationToken cancellationToken = default) => throw Unsupported();

    public IAsyncEnumerable<DocumentChange<T>> NotifyOnChange<T>(CancellationToken cancellationToken = default) where T : class => throw Unsupported();
    public Task<IAsyncDisposable> SubscribeChanges<T>(Func<DocumentChange<T>, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) where T : class => throw Unsupported();
}
#endif
