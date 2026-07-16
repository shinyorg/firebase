#if IOS
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Native = Shiny.Firebase.Firestore.iOS.Binding;

namespace Shiny.DocumentDb.Firestore.Mobile;

// Real on-device adapter over the native Firebase Firestore SDK, via the slim Swift binding
// (Shiny.Firebase.Firestore.iOS.Binding). Documents cross the native boundary as JSON strings, so unlike the
// Android head there is no field-map converter here — the wrapper does that conversion natively.
//
// Feature parity with the Android head is deliberate: the same operations are implemented, and the same ones
// throw NotSupportedException until a later milestone.
public partial class MobileFirestoreDocumentStore
{
    partial void InitializePlatform()
    {
        // An app bundling GoogleService-Info.plist has usually already called configure(); that wins.
        if (!Native.Firestore.IsConfigured())
        {
            if (this.options.ProjectId is { Length: > 0 } projectId &&
                this.options.AppId is { Length: > 0 } appId)
            {
                Native.Firestore.Configure(projectId, appId, this.options.ApiKey ?? "");
            }
            else
            {
                Native.Firestore.ConfigureDefault();
            }
        }

        if (!Native.Firestore.IsConfigured())
            throw new InvalidOperationException(
                "FirebaseApp is not initialized. Bundle GoogleService-Info.plist (auto-init), call FirebaseApp.Configure before resolving the store, or set ProjectId/AppId on MobileFirestoreOptions.");

        var host = (string?)null;
        var port = 8080;
        if (this.options.EmulatorHost is { Length: > 0 } emulator)
        {
            var parts = emulator.Split(':');
            host = parts[0];
            if (parts.Length > 1)
                port = int.Parse(parts[1]);
        }

        // Settings must be applied before any operation — the native SDK forbids mutating them after use.
        Native.Firestore.ApplySettings(this.options.PersistenceEnabled, host, port);
    }

    internal JsonSerializerOptions? JsonOpts => this.options.JsonSerializerOptions;

    // Firestore stores the serialized JSON key, so a query field must use the JSON property name.
    internal string FieldName<T>(string memberName)
        => this.options.JsonSerializerOptions?.PropertyNamingPolicy?.ConvertName(memberName) ?? memberName;

    internal IReadOnlyList<Internal.QueryFilter> GlobalFilters(Type type) => this.options.ResolveQueryFilters(type);

    // ── Serialization / id ──────────────────────────────────────────────
    JsonObject ToJson<T>(T document, JsonTypeInfo<T>? typeInfo) where T : class
    {
        var node = typeInfo != null
            ? JsonSerializer.SerializeToNode(document, typeInfo)
            : JsonSerializer.SerializeToNode(document, this.options.JsonSerializerOptions);
        return node as JsonObject
            ?? throw new InvalidOperationException($"Document of type {typeof(T).Name} did not serialize to a JSON object.");
    }

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

    internal T? Deserialize<T>(string json, JsonTypeInfo<T>? typeInfo) where T : class
        => typeInfo != null
            ? JsonSerializer.Deserialize(json, typeInfo)
            : JsonSerializer.Deserialize<T>(json, this.options.JsonSerializerOptions);

    // ── Writes ──────────────────────────────────────────────────────────
    public async Task Insert<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        // v1: Firestore set() is an upsert; true insert-if-absent (transactional) is a later milestone.
        => await this.WriteAsync(document, jsonTypeInfo).ConfigureAwait(false);

    public async Task Update<T>(T document, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        => await this.WriteAsync(document, jsonTypeInfo).ConfigureAwait(false);

    public async Task Upsert<T>(T patch, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
        => await this.WriteAsync(patch, jsonTypeInfo).ConfigureAwait(false);

    async Task WriteAsync<T>(T document, JsonTypeInfo<T>? typeInfo) where T : class
    {
        var json = this.ToJson(document, typeInfo);
        var id = this.ExtractId<T>(json);
        await Native.Firestore
            .SetDocumentAsync(this.ResolveCollection<T>(), id, json.ToJsonString())
            .ConfigureAwait(false);
    }

    public async Task<bool> Remove<T>(object id, CancellationToken cancellationToken = default) where T : class
    {
        await Native.Firestore
            .DeleteDocumentAsync(this.ResolveCollection<T>(), id.ToString()!)
            .ConfigureAwait(false);
        return true; // Firestore delete is idempotent and does not report whether a doc existed.
    }

    public async Task<int> Clear<T>(CancellationToken cancellationToken = default) where T : class
    {
        var collection = this.ResolveCollection<T>();
        var ids = await this.ReadIds(collection).ConfigureAwait(false);
        foreach (var id in ids)
            await Native.Firestore.DeleteDocumentAsync(collection, id).ConfigureAwait(false);
        return ids.Count;
    }

    // ── Reads ───────────────────────────────────────────────────────────
    public async Task<T?> Get<T>(object id, JsonTypeInfo<T>? jsonTypeInfo = null, CancellationToken cancellationToken = default) where T : class
    {
        var json = await Native.Firestore
            .GetDocumentAsync(this.ResolveCollection<T>(), id.ToString()!)
            .ConfigureAwait(false);

        // The wrapper returns null (not an error) when the document does not exist.
        var value = json?.ToString();
        return string.IsNullOrEmpty(value) ? null : this.Deserialize<T>(value, jsonTypeInfo);
    }

    async Task<IReadOnlyList<string>> ReadIds(string collection)
    {
        var json = (await Native.Firestore.Query(collection).GetDocumentsAsync().ConfigureAwait(false))?.ToString();
        return ParseIds(json);
    }

    // The wrapper hands back a JSON array of { "id": …, "data": { … } }.
    internal static IReadOnlyList<string> ParseIds(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in JsonNode.Parse(json)!.AsArray())
        {
            var id = item?["id"]?.GetValue<string>();
            if (id != null)
                list.Add(id);
        }
        return list;
    }

    internal IReadOnlyList<T> ParseDocuments<T>(string? json, JsonTypeInfo<T>? typeInfo) where T : class
    {
        if (string.IsNullOrEmpty(json))
            return Array.Empty<T>();

        var list = new List<T>();
        foreach (var item in JsonNode.Parse(json)!.AsArray())
        {
            var data = item?["data"];
            if (data == null)
                continue;
            var doc = this.Deserialize<T>(data.ToJsonString(), typeInfo);
            if (doc != null)
                list.Add(doc);
        }
        return list;
    }

    // ── Query ───────────────────────────────────────────────────────────
    public IDocumentQuery<T> Query<T>(JsonTypeInfo<T>? jsonTypeInfo = null) where T : class
        => new MobileFirestoreQuery<T>(this, this.ResolveCollection<T>(), jsonTypeInfo);

    public async Task<int> Count<T>(string? whereClause = null, object? parameters = null, CancellationToken cancellationToken = default) where T : class
    {
        if (whereClause != null)
            throw NotYet("string WHERE Count<T>()");
        return (await this.ReadIds(this.ResolveCollection<T>()).ConfigureAwait(false)).Count;
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
