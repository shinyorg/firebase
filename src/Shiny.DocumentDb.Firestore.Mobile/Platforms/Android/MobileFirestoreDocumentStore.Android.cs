#if ANDROID
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Android.Runtime;
using Com.Google.Firebase.Firestore;
using FirebaseApp = Com.Google.Firebase.FirebaseApp;
using Object = Java.Lang.Object;

namespace Shiny.DocumentDb.Firestore.Mobile;

// Real on-device adapter over the native Firebase Firestore SDK (Shiny.Firebase.Firestore.Binding.Android).
// v1 surface: CRUD (Insert/Update/Upsert/Get/Remove/Clear). Query translation, property patches, batch, diff,
// versioned CAS, and the change feed land in later milestones (throw NotSupportedException until then).
public partial class MobileFirestoreDocumentStore
{
    FirebaseFirestore firestore = null!;

    partial void InitializePlatform()
    {
        var app = FirebaseApp.Instance
            ?? throw new InvalidOperationException(
                "FirebaseApp is not initialized. Bundle google-services.json (auto-init) or call FirebaseApp.InitializeApp before resolving the store.");

        this.firestore = FirebaseFirestore.GetInstance(app);

        if (!string.IsNullOrWhiteSpace(this.options.EmulatorHost))
        {
            var parts = this.options.EmulatorHost!.Split(':');
            this.firestore.UseEmulator(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 8080);
        }

        // Apply the offline persistence setting, preserving any emulator host set above (copy-ctor).
        var settings = new FirebaseFirestoreSettings.Builder(this.firestore.FirestoreSettings)
            .SetPersistenceEnabled(this.options.PersistenceEnabled)
            .Build();
        this.firestore.FirestoreSettings = settings;
    }

    CollectionReference Collection<T>() where T : class => this.firestore.Collection(this.ResolveCollection<T>());

    internal System.Text.Json.JsonSerializerOptions? JsonOpts => this.options.JsonSerializerOptions;

    // Firestore stores the serialized JSON key, so a query field must use the JSON property name.
    internal string FieldName<T>(string memberName)
        => this.options.JsonSerializerOptions?.PropertyNamingPolicy?.ConvertName(memberName) ?? memberName;

    internal IReadOnlyList<Internal.QueryFilter> GlobalFilters(Type type) => this.options.ResolveQueryFilters(type);

    // ── Serialization / id ──────────────────────────────────────────────
    JsonObject ToJson<T>(T document, JsonTypeInfo<T>? typeInfo) where T : class
        => FirestoreJson.Serialize(document, typeInfo, this.options.JsonSerializerOptions);

    string IdPropertyName<T>() => this.options.ResolveIdPropertyName(typeof(T)) ?? "Id";

    string ExtractId<T>(JsonObject json) where T : class
    {
        var name = this.IdPropertyName<T>();
        // Match case-insensitively — the serialized name may be camelCased by the naming policy.
        foreach (var kv in json)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                var id = kv.Value?.ToString();
                if (!string.IsNullOrEmpty(id))
                    return id;
            }
        }
        throw new InvalidOperationException($"Document of type {typeof(T).Name} has no non-empty '{name}' to use as the Firestore document id.");
    }

    // ── Writes ──────────────────────────────────────────────────────────
    public async Task Insert<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        // v1: Firestore set() is an upsert; true insert-if-absent (transactional) is a later milestone.
        await this.WriteAsync(document, jsonTypeInfo).ConfigureAwait(false);
    }

    public async Task Update<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        => await this.WriteAsync(document, jsonTypeInfo).ConfigureAwait(false);

    public async Task Upsert<T>(T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        => await this.WriteAsync(patch, jsonTypeInfo).ConfigureAwait(false);

    async Task WriteAsync<T>(T document, JsonTypeInfo<T>? typeInfo) where T : class
    {
        var json = this.ToJson(document, typeInfo);
        var id = this.ExtractId<T>(json);
        var map = FirestoreValueConverter.ToJavaMap(json);
        await this.Collection<T>().Document(id).Set(map).AsVoidAsync().ConfigureAwait(false);
    }

    public async Task<bool> Remove<T>(object id, CancellationToken cancellationToken = default) where T : class
    {
        await this.Collection<T>().Document(id.ToString()!).Delete().AsVoidAsync().ConfigureAwait(false);
        return true; // Firestore delete is idempotent and does not report whether a doc existed.
    }

    public async Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
    {
        var snapshotObj = await this.Collection<T>().Get().AsAsync().ConfigureAwait(false);
        var snapshot = snapshotObj.JavaCast<QuerySnapshot>();
        var docs = snapshot.Documents;
        var count = 0;
        foreach (var doc in docs)
        {
            await doc.Reference.Delete().AsVoidAsync().ConfigureAwait(false);
            count++;
        }
        return count;
    }

    // ── Reads ───────────────────────────────────────────────────────────
    public async Task<T?> Get<T>(object id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var snapshotObj = await this.Collection<T>().Document(id.ToString()!).Get().AsAsync().ConfigureAwait(false);
        var snapshot = snapshotObj.JavaCast<DocumentSnapshot>();
        if (!snapshot.Exists())
            return null;

        var data = snapshot.Data;
        if (data == null)
            return null;

        var json = FirestoreValueConverter.ToJsonObject(data);
        return FirestoreJson.Deserialize(json, jsonTypeInfo, this.options.JsonSerializerOptions);
    }

    // ── Query ───────────────────────────────────────────────────────────
    public IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class
        => new MobileFirestoreQuery<T>(this, this.Collection<T>(), jsonTypeInfo);

    public async Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        if (whereClause != null)
            throw NotYet("string WHERE Count<T>()");
        var snapshotObj = await this.Collection<T>().Get().AsAsync().ConfigureAwait(false);
        return snapshotObj.JavaCast<QuerySnapshot>().Documents.Count;
    }

    // String-WHERE surface (the SQL-ish grammar) isn't ported to Firestore's structured queries in v1.
    public Task<IReadOnlyList<T>> Query<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("string Query<T>()");
    public IAsyncEnumerable<T> QueryStream<T>(string whereClause, JsonTypeInfo<T>? jsonTypeInfo = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("string QueryStream<T>()");
    public Task<JsonPatchDocument<T>?> GetDiff<T>(object id, T modified, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("GetDiff<T>()");
    public Task<int> BatchInsert<T>(IEnumerable<T> documents, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("BatchInsert<T>()");
    public Task<bool> SetProperty<T>(object id, Expression<Func<T, object>> property, object? value, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("SetProperty<T>()");
    public Task<bool> RemoveProperty<T>(object id, Expression<Func<T, object>> property, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class => throw NotYet("RemoveProperty<T>()");
    public Task ClearAll(CancellationToken cancellationToken = default) => throw NotYet("ClearAll()");

    static NotSupportedException NotYet(string what) => new($"{what} is not yet implemented in the mobile Firestore provider (planned for a later milestone).");
}
#endif
