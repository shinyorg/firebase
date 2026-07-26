#if ANDROID
using System.Linq.Expressions;
using Com.Google.Firebase.Firestore;
using Object = Java.Lang.Object;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Translates a LINQ predicate (<c>Expression&lt;Func&lt;T,bool&gt;&gt;</c>) into native Firestore
/// <c>where*</c> filters. Supports the equality/comparison operators and <c>&amp;&amp;</c> composition, plus
/// <c>ICollection.Contains</c> → <c>array-contains</c>. Firestore can't express arbitrary predicates
/// (no <c>||</c> across fields, no computed expressions), so anything outside this set throws
/// <see cref="NotSupportedException"/> — the caller keeps the LINQ contract honest rather than silently
/// returning wrong results.
/// </summary>
static class FirestoreQueryTranslator
{
    public static Query Apply(Query query, Expression body, Func<string, string> field)
    {
        switch (body)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso } and:
                query = Apply(query, and.Left, field);
                return Apply(query, and.Right, field);

            case BinaryExpression be:
                var (name, value) = ExtractComparison(be, field);
                var jv = ToJava(value);
                return be.NodeType switch
                {
                    ExpressionType.Equal => query.WhereEqualTo(name, jv),
                    ExpressionType.NotEqual => query.WhereNotEqualTo(name, jv),
                    ExpressionType.GreaterThan => query.WhereGreaterThan(name, jv!),
                    ExpressionType.GreaterThanOrEqual => query.WhereGreaterThanOrEqualTo(name, jv!),
                    ExpressionType.LessThan => query.WhereLessThan(name, jv!),
                    ExpressionType.LessThanOrEqual => query.WhereLessThanOrEqualTo(name, jv!),
                    _ => throw Unsupported(be.NodeType.ToString())
                };

            // x.Tags.Contains(value)  →  array-contains
            case MethodCallExpression { Method.Name: "Contains" } mc when mc.Object is MemberExpression me && mc.Arguments.Count == 1:
                return query.WhereArrayContains(field(me.Member.Name), ToJava(Evaluate(mc.Arguments[0]))!);

            // bool member  →  == true
            case MemberExpression m when m.Type == typeof(bool):
                return query.WhereEqualTo(field(m.Member.Name), Java.Lang.Boolean.ValueOf(true));

            case UnaryExpression { NodeType: ExpressionType.Not, Operand: MemberExpression nm } when nm.Type == typeof(bool):
                return query.WhereEqualTo(field(nm.Member.Name), Java.Lang.Boolean.ValueOf(false));

            default:
                throw Unsupported(body.NodeType.ToString());
        }
    }

    static (string Field, object? Value) ExtractComparison(BinaryExpression be, Func<string, string> field)
    {
        if (StripConvert(be.Left) is MemberExpression lm && lm.Expression is ParameterExpression)
            return (field(lm.Member.Name), Evaluate(be.Right));
        if (StripConvert(be.Right) is MemberExpression rm && rm.Expression is ParameterExpression)
            return (field(rm.Member.Name), Evaluate(be.Left));
        throw Unsupported("comparison must be between a document property and a constant");
    }

    static Expression StripConvert(Expression e)
        => e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u ? u.Operand : e;

    static object? Evaluate(Expression e) => QueryValueEvaluator.Evaluate(e);

    internal static Object? ToJava(object? value) => value switch
    {
        null => null,
        string s => new Java.Lang.String(s),
        bool b => Java.Lang.Boolean.ValueOf(b),
        int i => Java.Lang.Long.ValueOf(i),
        long l => Java.Lang.Long.ValueOf(l),
        short sh => Java.Lang.Long.ValueOf(sh),
        byte by => Java.Lang.Long.ValueOf(by),
        double d => Java.Lang.Double.ValueOf(d),
        float f => Java.Lang.Double.ValueOf(f),
        decimal m => Java.Lang.Double.ValueOf((double)m),
        Enum en => Java.Lang.Long.ValueOf(Convert.ToInt64(en)), // STJ default serializes enums as numbers
        DateTime dt => new Java.Lang.String(dt.ToString("o")),
        DateTimeOffset dto => new Java.Lang.String(dto.ToString("o")),
        Guid g => new Java.Lang.String(g.ToString()),
        _ => throw Unsupported($"value type {value.GetType().Name}")
    };

    static NotSupportedException Unsupported(string what)
        => new($"The mobile Firestore query provider can't translate '{what}'. Supported: ==, !=, <, <=, >, >=, && composition, and ICollection.Contains (array-contains).");
}
#endif
