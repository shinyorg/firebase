using System.Linq.Expressions;
using System.Text.Json;
using Shiny.DocumentDb.Firestore.Mobile;
using Xunit;

namespace Shiny.DocumentDb.Tests.MobileFirestore;

// The AOT-safe query paths replaced two pieces of framework machinery: JsonSerializer.Serialize(object) in the
// iOS translator, and Expression.Compile().DynamicInvoke() in both translators. These tests are differential —
// each asserts the replacement produces exactly what the thing it replaced produced, since a silent drift here
// would mean stored documents stop matching their own query predicates.
public class AotReplacementTests
{
    enum Suit { Clubs = 0, Hearts = 3 }

    public static TheoryData<object?> QueryValues => new()
    {
        null,
        "text",
        "quote\"and\\slash",
        true,
        false,
        42,
        -1,
        7L,
        (short)3,
        (byte)9,
        1.5d,
        2.5f,
        3.25m,
        Suit.Hearts,
        Suit.Clubs,
        new DateTime(2026, 7, 26, 13, 45, 12, DateTimeKind.Utc),
        new DateTimeOffset(2026, 7, 26, 13, 45, 12, TimeSpan.FromHours(-4)),
        Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        new List<object?> { 1, "two", true },
        new object?[] { 1.5, null, "x" }
    };

    [Theory]
    [MemberData(nameof(QueryValues))]
    public void SerializeQueryValue_MatchesJsonSerializer(object? value)
        => Assert.Equal(JsonSerializer.Serialize(value), FirestoreJson.SerializeQueryValue(value));

    [Fact]
    public void SerializeQueryValue_RejectsUntranslatableType()
        => Assert.Throws<NotSupportedException>(() => FirestoreJson.SerializeQueryValue(new object()));

    // ── Expression evaluation ───────────────────────────────────────────

    static object? ViaCompile(Expression e)
        => Expression.Lambda(Expression.Convert(e, typeof(object))).Compile().DynamicInvoke();

    static void AssertMatchesCompile(Expression e)
        => Assert.Equal(ViaCompile(e), QueryValueEvaluator.Evaluate(e));

    static Expression RightOf<T>(Expression<Func<T, bool>> predicate)
        => ((BinaryExpression)predicate.Body).Right;

    class Doc
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public DateTime Created { get; set; }
    }

    class Holder
    {
        public string Value = "field-value";
        public string Property { get; set; } = "property-value";
    }

    static string StaticMethod(string s) => s.ToUpperInvariant();

    [Fact]
    public void Evaluate_Literal()
        => AssertMatchesCompile(RightOf<Doc>(x => x.Count == 5));

    [Fact]
    public void Evaluate_CapturedLocal()
    {
        var cutoff = 17;
        AssertMatchesCompile(RightOf<Doc>(x => x.Count == cutoff));
    }

    [Fact]
    public void Evaluate_CapturedLocalOfReferenceType()
    {
        var name = "shiny";
        AssertMatchesCompile(RightOf<Doc>(x => x.Name == name));
    }

    [Fact]
    public void Evaluate_FieldOnCapturedObject()
    {
        var holder = new Holder();
        AssertMatchesCompile(RightOf<Doc>(x => x.Name == holder.Value));
    }

    [Fact]
    public void Evaluate_PropertyOnCapturedObject()
    {
        var holder = new Holder();
        AssertMatchesCompile(RightOf<Doc>(x => x.Name == holder.Property));
    }

    [Fact]
    public void Evaluate_StaticProperty()
        => Assert.IsType<DateTime>(QueryValueEvaluator.Evaluate(RightOf<Doc>(x => x.Created == DateTime.MinValue)));

    [Fact]
    public void Evaluate_MethodCall()
    {
        var name = "shiny";
        AssertMatchesCompile(RightOf<Doc>(x => x.Name == StaticMethod(name)));
    }

    [Fact]
    public void Evaluate_InstanceMethodCall()
    {
        var name = "Shiny";
        AssertMatchesCompile(RightOf<Doc>(x => x.Name == name.ToLowerInvariant()));
    }

    [Fact]
    public void Evaluate_Conversion()
    {
        short small = 12;
        AssertMatchesCompile(RightOf<Doc>(x => x.Count == small));
    }

    // The shape ApplyContains hits for `new[] { … }.Contains(x.Prop)`. Since .NET 10 this binds to
    // MemoryExtensions.Contains, so the collection argument is an array→ReadOnlySpan conversion rather than
    // the array itself — a shape the old Expression.Compile path could not evaluate at all (see below).
    [Fact]
    public void Evaluate_InlineArrayThroughSpanConversion()
    {
        Expression<Func<Doc, bool>> e = x => new[] { "a", "b" }.Contains(x.Name);
        var collection = ((MethodCallExpression)e.Body).Arguments[0];

        Assert.Equal(new object?[] { "a", "b" }, QueryValueEvaluator.Evaluate(collection));
    }

    // Pins why the above needs special handling: a ByRef-like value cannot be boxed into object, so the
    // Expression.Compile approach this replaced threw on an inline-array Contains regardless of AOT.
    [Fact]
    public void Evaluate_SpanConversion_WasUnsupportedByCompilePath()
    {
        Expression<Func<Doc, bool>> e = x => new[] { "a", "b" }.Contains(x.Name);
        var collection = ((MethodCallExpression)e.Body).Arguments[0];

        Assert.ThrowsAny<Exception>(() => ViaCompile(collection));
    }

    [Fact]
    public void Evaluate_CapturedCollectionForContains()
    {
        var allowed = new List<string> { "a", "b" };
        Expression<Func<Doc, bool>> e = x => allowed.Contains(x.Name);
        var call = (MethodCallExpression)e.Body;

        Assert.Equal(allowed, QueryValueEvaluator.Evaluate(call.Object!));
    }
}
