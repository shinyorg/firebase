#if IOS
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Native = Shiny.Firebase.Firestore.iOS.Binding;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Native-Firestore-backed <see cref="IDocumentQuery{T}"/> for iOS. Mirrors the Android implementation:
/// filters/ordering/limit push down to the native query; aggregates and the paging offset are applied
/// client-side over the materialized results. Projection, grouping, vector, and full-text throw.
/// </summary>
sealed class MobileFirestoreQuery<T> : IDocumentQuery<T> where T : class
{
    readonly MobileFirestoreDocumentStore store;
    readonly string collection;
    readonly JsonTypeInfo<T>? typeInfo;
    Native.FirestoreQuery query;
    int skip;

    public MobileFirestoreQuery(
        MobileFirestoreDocumentStore store,
        string collection,
        JsonTypeInfo<T>? typeInfo,
        bool ignoreGlobalFilters = false)
    {
        this.store = store;
        this.collection = collection;
        this.typeInfo = typeInfo;
        this.query = Native.Firestore.Query(collection);

        if (!ignoreGlobalFilters)
            foreach (var f in store.GlobalFilters(typeof(T)))
                this.query = FirestoreQueryTranslator.Apply(this.query, f.Predicate.Body, this.Field);
    }

    public JsonTypeInfo<T>? QueryTypeInfo => this.typeInfo;

    string Field(string member) => this.store.FieldName<T>(member);

    public IDocumentQuery<T> Where(Expression<Func<T, bool>> predicate)
    {
        this.query = FirestoreQueryTranslator.Apply(this.query, predicate.Body, this.Field);
        return this;
    }

    public IDocumentQuery<T> IgnoreQueryFilters()
        => new MobileFirestoreQuery<T>(this.store, this.collection, this.typeInfo, ignoreGlobalFilters: true);

    public IDocumentQuery<T> IgnoreQueryFilters(params string[] filterNames)
        => this.IgnoreQueryFilters(); // v1: all-or-nothing (named removal not tracked)

    public IDocumentQuery<T> OrderBy(Expression<Func<T, object>> selector)
    {
        this.query = this.query.OrderBy(this.Field(MemberName(selector)), false);
        return this;
    }

    public IDocumentQuery<T> OrderByDescending(Expression<Func<T, object>> selector)
    {
        this.query = this.query.OrderBy(this.Field(MemberName(selector)), true);
        return this;
    }

    public IDocumentQuery<T> Paginate(int offset, int take)
    {
        this.skip = offset;
        this.query = this.query.LimitTo(offset + take); // Firestore has no offset; skip client-side in ToList
        return this;
    }

    async Task<string?> Fetch()
        => (await this.query.GetDocumentsAsync().ConfigureAwait(false))?.ToString();

    public async Task<IReadOnlyList<T>> ToList(CancellationToken ct = default)
    {
        var items = this.store.ParseDocuments(await this.Fetch().ConfigureAwait(false), this.typeInfo);
        return this.skip > 0 ? items.Skip(this.skip).ToList() : items;
    }

    public async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in await this.ToList(ct).ConfigureAwait(false))
            yield return item;
    }

    public async Task<long> Count(CancellationToken ct = default)
        => (await this.ToList(ct).ConfigureAwait(false)).Count;

    public async Task<bool> Any(CancellationToken ct = default)
    {
        var json = (await this.query.LimitTo(1).GetDocumentsAsync().ConfigureAwait(false))?.ToString();
        return MobileFirestoreDocumentStore.ParseIds(json).Count > 0;
    }

    public async Task<int> ExecuteDelete(CancellationToken ct = default)
    {
        var ids = MobileFirestoreDocumentStore.ParseIds(await this.Fetch().ConfigureAwait(false));
        foreach (var id in ids)
            await Native.Firestore.DeleteDocumentAsync(this.collection, id).ConfigureAwait(false);
        return ids.Count;
    }

    public async Task<int> ExecuteUpdate(Expression<Func<T, object>> property, object? value, CancellationToken ct = default)
    {
        var field = this.Field(MemberName(property));
        var json = FirestoreQueryTranslator.ToJson(value);
        var ids = MobileFirestoreDocumentStore.ParseIds(await this.Fetch().ConfigureAwait(false));
        foreach (var id in ids)
            await Native.Firestore.UpdateFieldAsync(this.collection, id, field, json).ConfigureAwait(false);
        return ids.Count;
    }

    public async Task<TValue> Max<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default)
    {
        var sel = selector.Compile();
        return (await this.ToList(ct).ConfigureAwait(false)).Select(sel).Max()!;
    }

    public async Task<TValue> Min<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default)
    {
        var sel = selector.Compile();
        return (await this.ToList(ct).ConfigureAwait(false)).Select(sel).Min()!;
    }

    public async Task<TValue> Sum<TValue>(Expression<Func<T, TValue>> selector, CancellationToken ct = default)
    {
        var sel = selector.Compile();
        var total = (await this.ToList(ct).ConfigureAwait(false)).Sum(x => Convert.ToDouble(sel(x)));
        return (TValue)Convert.ChangeType(total, typeof(TValue));
    }

    public async Task<double> Average(Expression<Func<T, object>> selector, CancellationToken ct = default)
    {
        var sel = selector.Compile();
        var items = await this.ToList(ct).ConfigureAwait(false);
        return items.Count == 0 ? 0d : items.Average(x => Convert.ToDouble(sel(x)));
    }

    public IAsyncEnumerable<DocumentChange<T>> NotifyOnChange(CancellationToken ct = default)
        => this.store.ListenQuery(this.query, this.typeInfo, ct);

    public IDocumentQuery<TResult> Select<TResult>(Expression<Func<T, TResult>> selector, JsonTypeInfo<TResult>? resultTypeInfo = null) where TResult : class
        => throw new NotSupportedException(
            "Select projection is not supported by the mobile Firestore provider. Read with ToList and project client-side.");

    static string MemberName(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
            body = u.Operand;
        if (body is MemberExpression m)
            return m.Member.Name;
        throw new NotSupportedException("Selector must be a simple property access (e.g. x => x.Name).");
    }
}
#endif
