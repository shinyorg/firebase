using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Evaluates the constant side of a translated predicate — the value in <c>x.Prop == value</c> — without
/// generating code, so a captured local survives full AOT.
/// </summary>
/// <remarks>
/// The obvious implementation is <c>Expression.Lambda(...).Compile().DynamicInvoke()</c>, but that needs a
/// runtime code generator. Mono papers over it with the expression interpreter, so it works on MAUI iOS today
/// and fails under NativeAOT. Every shape that actually turns up in a query — a literal, a captured local
/// (a field read on a closure), a field/property chain, a method call, an inline array — is walked directly
/// instead. The compile path stays as a last resort, guarded by
/// <see cref="RuntimeFeature.IsDynamicCodeSupported"/> so it is skipped rather than crashing where codegen is
/// unavailable.
/// <para>
/// The reflection here is trim-safe: every <see cref="MemberInfo"/> comes out of the expression tree, which
/// already roots it, so nothing is resolved by name.
/// </para>
/// </remarks>
static class QueryValueEvaluator
{
    public static object? Evaluate(Expression e)
    {
        switch (e)
        {
            case ConstantExpression c:
                return c.Value;

            // A captured local compiles to a field read on a closure instance; static fields have no target.
            case MemberExpression { Member: FieldInfo field } m:
                return field.GetValue(m.Expression is null ? null : Evaluate(m.Expression));

            case MemberExpression { Member: PropertyInfo property } m:
                return property.GetValue(m.Expression is null ? null : Evaluate(m.Expression));

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u:
                return ConvertTo(Evaluate(u.Operand), u.Type);

            // A span conversion, which is how `new[] { … }.Contains(x.Prop)` arrives: since .NET 10 that binds
            // to MemoryExtensions.Contains, so the collection is wrapped in an array→ReadOnlySpan call. The
            // conversion is not reflectable (nor boxable, so the old compile path threw here too) — step
            // straight to the thing being converted, which is the collection we actually want.
            case MethodCallExpression { Method.ReturnType.IsByRefLike: true } span:
                return Evaluate(span.Object ?? span.Arguments[0]);

            case MethodCallExpression call:
                return call.Method.Invoke(
                    call.Object is null ? null : Evaluate(call.Object),
                    call.Arguments.Select(Evaluate).ToArray());

            // new[] { a, b, c } — boxed into object[] so the callers' runtime-type switches still see the
            // element types. Array.CreateInstance would itself require dynamic code.
            case NewArrayExpression { NodeType: ExpressionType.NewArrayInit } na:
                return na.Expressions.Select(Evaluate).ToArray();

            default:
                return Fallback(e);
        }
    }

    static object? Fallback(Expression e)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new NotSupportedException(
                $"The mobile Firestore provider cannot evaluate '{e.NodeType}' in a query without runtime code " +
                "generation, which this build does not support. Hoist the value into a local and compare " +
                "against that instead.");

        return Expression.Lambda(Expression.Convert(e, typeof(object))).Compile().DynamicInvoke();
    }

    static object? ConvertTo(object? value, Type target)
    {
        if (value is null || target.IsInstanceOfType(value))
            return value;

        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t.IsEnum)
            return Enum.ToObject(t, value);

        return value is IConvertible ? Convert.ChangeType(value, t, CultureInfo.InvariantCulture) : value;
    }
}
