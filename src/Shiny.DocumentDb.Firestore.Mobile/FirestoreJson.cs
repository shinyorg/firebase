using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// The provider's single document (de)serialization seam. Both platform adapters, the query, and the change
/// listener funnel through here so the "typed when the caller gave us a <see cref="JsonTypeInfo{T}"/>,
/// reflection otherwise" decision lives in one place instead of six copies of the same ternary.
/// </summary>
/// <remarks>
/// The reflection branch is what <see cref="MobileFirestoreOptions.UseReflectionFallback"/> exists to switch
/// off: with it false the store never hands a null <c>typeInfo</c> down here, so on a trimmed or full-AOT
/// build only the source-generated path is reachable. That is what the suppressions below assert — they are
/// scoped to these three methods rather than smeared across the call sites.
/// </remarks>
static class FirestoreJson
{
    const string Justification =
        "The reflection branch runs only when typeInfo is null, which UseReflectionFallback=false prevents on trimmed/AOT builds.";

    /// <summary>Serializes a document to its Firestore field map.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public static JsonObject Serialize<T>(T document, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions? options) where T : class
    {
        var node = typeInfo != null
            ? JsonSerializer.SerializeToNode(document, typeInfo)
            : JsonSerializer.SerializeToNode(document, options);

        return node as JsonObject
            ?? throw new InvalidOperationException($"Document of type {typeof(T).Name} did not serialize to a JSON object.");
    }

    /// <summary>Materializes a document from a Firestore field map already converted to JSON.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public static T? Deserialize<T>(JsonNode node, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions? options) where T : class
        => typeInfo != null
            ? node.Deserialize(typeInfo)
            : node.Deserialize<T>(options);

    /// <summary>Materializes a document from raw JSON text (the iOS binding hands back strings).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = Justification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = Justification)]
    public static T? Deserialize<T>(string json, JsonTypeInfo<T>? typeInfo, JsonSerializerOptions? options) where T : class
        => typeInfo != null
            ? JsonSerializer.Deserialize(json, typeInfo)
            : JsonSerializer.Deserialize<T>(json, options);

    /// <summary>
    /// Serializes a query comparison value — the right-hand side of <c>x.Prop == value</c> — for the iOS
    /// binding, which takes its filters as <c>(field, op, jsonValue)</c>.
    /// </summary>
    /// <remarks>
    /// Written out by hand rather than via <c>JsonSerializer.Serialize(object)</c>, which would need the
    /// runtime type resolved reflectively. The accepted set mirrors the Android translator's <c>ToJava</c>
    /// switch, so a value either translates identically on both platforms or is rejected on both. The
    /// <see cref="Utf8JsonWriter"/> overloads for date and Guid emit the same ISO-8601 / "D" forms
    /// <c>JsonSerializer</c> did, so stored comparisons keep matching.
    /// </remarks>
    public static string SerializeQueryValue(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteQueryValue(writer, value);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static void WriteQueryValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case short sh: w.WriteNumberValue(sh); break;
            case byte by: w.WriteNumberValue(by); break;
            case double d: w.WriteNumberValue(d); break;
            case float f: w.WriteNumberValue(f); break;
            case decimal m: w.WriteNumberValue(m); break;
            case Enum en: w.WriteNumberValue(Convert.ToInt64(en)); break; // STJ default serializes enums as numbers
            case DateTime dt: w.WriteStringValue(dt); break;
            case DateTimeOffset dto: w.WriteStringValue(dto); break;
            case Guid g: w.WriteStringValue(g); break;

            case IEnumerable items:
                w.WriteStartArray();
                foreach (var item in items)
                    WriteQueryValue(w, item);
                w.WriteEndArray();
                break;

            default:
                throw new NotSupportedException(
                    $"The mobile Firestore provider cannot translate a query value of type '{value.GetType().Name}'.");
        }
    }
}
