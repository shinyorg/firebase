#if ANDROID
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Android.Runtime;
using Com.Google.Firebase.Firestore;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Native-Firestore-backed <see cref="IDocumentQuery{T}"/>. Filters/ordering/limit push down to the native
/// query; aggregates and paging offset are applied client-side over the materialized results. Projection,
/// grouping, cursor paging, vector, and full-text are not supported (they throw).
/// </summary>
sealed class MobileFirestoreQuery<T> : IDocumentQuery<T> where T : class
{
    readonly MobileFirestoreDocumentStore store;
    readonly CollectionReference collection;
    readonly JsonTypeInfo<T>? typeInfo;
    Query query;
    int skip;

    public MobileFirestoreQuery(
        MobileFirestoreDocumentStore store,
        CollectionReference collection,
        JsonTypeInfo<T>? typeInfo,
        bool ignoreGlobalFilters = false)
    {
        this.store = store;
        this.collection = collection;
        this.typeInfo = typeInfo;
        this.query = collection;

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
        this.query = this.query.OrderBy(this.Field(MemberName(selector)));
        return this;
    }

    public IDocumentQuery<T> OrderByDescending(Expression<Func<T, object>> selector)
    {
        this.query = this.query.OrderBy(this.Field(MemberName(selector)), Query.Direction.Descending!);
        return this;
    }

    public IDocumentQuery<T> Paginate(int offset, int take)
    {
        this.skip = offset;
        this.query = this.query.Limit(offset + take); // Firestore has no offset; skip client-side in ToList
        return this;
    }

    public async Task<IReadOnlyList<T>> ToList(CancellationToken ct = default)
    {
        var snap = (await this.query.Get().AsAsync().ConfigureAwait(false)).JavaCast<QuerySnapshot>();
        var list = new List<T>();
        var i = 0;
        foreach (var doc in snap.Documents)
        {
            if (i++ < this.skip)
                continue;
            var data = doc.Data;
            if (data == null)
                continue;
            var json = FirestoreValueConverter.ToJsonObject(data);
            var item = this.typeInfo != null ? json.Deserialize(this.typeInfo) : json.Deserialize<T>(this.store.JsonOpts);
            if (item != null)
                list.Add(item);
        }
        return list;
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
        var snap = (await this.query.Limit(1).Get().AsAsync().ConfigureAwait(false)).JavaCast<QuerySnapshot>();
        return snap.Documents.Count > 0;
    }

    public async Task<int> ExecuteDelete(CancellationToken ct = default)
    {
        var snap = (await this.query.Get().AsAsync().ConfigureAwait(false)).JavaCast<QuerySnapshot>();
        var n = 0;
        foreach (var doc in snap.Documents)
        {
            await doc.Reference.Delete().AsVoidAsync().ConfigureAwait(false);
            n++;
        }
        return n;
    }

    public async Task<int> ExecuteUpdate(Expression<Func<T, object>> property, object? value, CancellationToken ct = default)
    {
        var field = this.Field(MemberName(property));
        var jv = FirestoreQueryTranslator.ToJava(value);
        var snap = (await this.query.Get().AsAsync().ConfigureAwait(false)).JavaCast<QuerySnapshot>();
        var n = 0;
        foreach (var doc in snap.Documents)
        {
            await doc.Reference.Update(field, jv).AsVoidAsync().ConfigureAwait(false);
            n++;
        }
        return n;
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
