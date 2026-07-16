#if IOS
using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;
using Native = Shiny.Firebase.Firestore.iOS.Binding;

namespace Shiny.DocumentDb.Firestore.Mobile;

// Translates a LINQ predicate into native Firestore where* calls, mirroring the Android translator. The slim
// binding takes (field, op, jsonValue), so comparison values are serialized to JSON rather than boxed into
// native types.
static class FirestoreQueryTranslator
{
    public static Native.FirestoreQuery Apply(
        Native.FirestoreQuery query,
        Expression body,
        Func<string, string> fieldName)
    {
        switch (body)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso } and:
                query = Apply(query, and.Left, fieldName);
                return Apply(query, and.Right, fieldName);

            case BinaryExpression binary:
                var op = binary.NodeType switch
                {
                    ExpressionType.Equal => "==",
                    ExpressionType.NotEqual => "!=",
                    ExpressionType.LessThan => "<",
                    ExpressionType.LessThanOrEqual => "<=",
                    ExpressionType.GreaterThan => ">",
                    ExpressionType.GreaterThanOrEqual => ">=",
                    _ => throw Unsupported(binary.NodeType.ToString())
                };
                var (field, value) = Resolve(binary, fieldName);
                return query.WhereField(field, op, ToJson(value));

            // ICollection.Contains(x.Prop) → whereField(in) — mirrors the Android translator.
            case MethodCallExpression { Method.Name: "Contains" } call:
                return ApplyContains(query, call, fieldName);

            default:
                throw Unsupported(body.NodeType.ToString());
        }
    }

    static Native.FirestoreQuery ApplyContains(
        Native.FirestoreQuery query,
        MethodCallExpression call,
        Func<string, string> fieldName)
    {
        // Both shapes: collection.Contains(x.Prop) (instance) and Enumerable.Contains(collection, x.Prop).
        var collection = call.Object ?? call.Arguments[0];
        var member = call.Object != null ? call.Arguments[0] : call.Arguments[1];

        if (Unwrap(member) is not MemberExpression m)
            throw Unsupported("Contains without a simple property access");

        var values = Evaluate(collection) as IEnumerable
            ?? throw Unsupported("Contains over a non-enumerable");

        var list = new List<object?>();
        foreach (var v in values)
            list.Add(v);

        return query.WhereField(fieldName(m.Member.Name), "in", ToJson(list));
    }

    // Returns (fieldName, constantValue) regardless of which side the member sits on.
    static (string Field, object? Value) Resolve(BinaryExpression binary, Func<string, string> fieldName)
    {
        if (Unwrap(binary.Left) is MemberExpression left && IsParameterBound(left))
            return (fieldName(left.Member.Name), Evaluate(binary.Right));

        if (Unwrap(binary.Right) is MemberExpression right && IsParameterBound(right))
            return (fieldName(right.Member.Name), Evaluate(binary.Left));

        throw Unsupported("comparison without a document property on either side");
    }

    // x.Prop → true; someLocal.Prop → false (that's a captured value, not a document field).
    static bool IsParameterBound(MemberExpression member)
        => Unwrap(member.Expression!) is ParameterExpression;

    static Expression Unwrap(Expression e)
        => e is UnaryExpression { NodeType: ExpressionType.Convert } u ? Unwrap(u.Operand) : e;

    static object? Evaluate(Expression e)
        => e is ConstantExpression c
            ? c.Value
            : Expression.Lambda(Expression.Convert(e, typeof(object))).Compile().DynamicInvoke();

    internal static string ToJson(object? value)
        => JsonSerializer.Serialize(value);

    static NotSupportedException Unsupported(string what)
        => new($"The mobile Firestore provider cannot translate '{what}' into a native Firestore query.");
}
#endif
