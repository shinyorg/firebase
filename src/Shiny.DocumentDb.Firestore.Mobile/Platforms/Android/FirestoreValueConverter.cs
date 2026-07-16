#if ANDROID
using System.Text.Json.Nodes;
using Java.Lang;
using Java.Util;
using Object = Java.Lang.Object;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Converts between DocumentDb's JSON document body and the native Firestore field map
/// (<c>Map&lt;String, Object&gt;</c> / native value types). Scalars, objects, and arrays are handled
/// recursively; Firestore server types (Timestamp/GeoPoint/Blob) are best-effort for the v1 adapter.
/// </summary>
static class FirestoreValueConverter
{
    // ── JSON → native (writes) ──────────────────────────────────────────
    public static HashMap ToJavaMap(JsonObject obj)
    {
        var map = new HashMap();
        foreach (var kv in obj)
            map.Put(kv.Key, ToJava(kv.Value));
        return map;
    }

    public static Object? ToJava(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;

            case JsonObject o:
                return ToJavaMap(o);

            case JsonArray a:
                var list = new ArrayList();
                foreach (var item in a)
                    list.Add(ToJava(item));
                return list;

            case JsonValue v:
                if (v.TryGetValue<bool>(out var b)) return Java.Lang.Boolean.ValueOf(b);
                if (v.TryGetValue<long>(out var l)) return Java.Lang.Long.ValueOf(l);
                if (v.TryGetValue<double>(out var d)) return Java.Lang.Double.ValueOf(d);
                if (v.TryGetValue<string>(out var s)) return new Java.Lang.String(s);
                // Fallback: stringify unknown scalar.
                return new Java.Lang.String(v.ToJsonString());

            default:
                return null;
        }
    }

    // ── native → JSON (reads) ───────────────────────────────────────────
    public static JsonObject ToJsonObject(IDictionary<string, Object?> data)
    {
        var obj = new JsonObject();
        foreach (var kv in data)
            obj[kv.Key] = ToJson(kv.Value);
        return obj;
    }

    public static JsonNode? ToJson(Object? value)
    {
        switch (value)
        {
            case null:
                return null;

            case Java.Lang.Boolean b:
                return JsonValue.Create(b.BooleanValue());

            case Java.Lang.Long l:
                return JsonValue.Create(l.LongValue());

            case Java.Lang.Integer i:
                return JsonValue.Create(i.LongValue());

            case Java.Lang.Double d:
                return JsonValue.Create(d.DoubleValue());

            case Java.Lang.Float f:
                return JsonValue.Create((double)f.FloatValue());

            case Java.Lang.String s:
                return JsonValue.Create(s.ToString());

            case IMap map:
                var obj = new JsonObject();
                foreach (var key in (System.Collections.IEnumerable)map.KeySet()!)
                {
                    var keyStr = key?.ToString() ?? "";
                    obj[keyStr] = ToJson(map.Get(new Java.Lang.String(keyStr)));
                }
                return obj;

            case IList list:
                var arr = new JsonArray();
                var lit = list.Iterator()!;
                while (lit.HasNext)
                    arr.Add(ToJson(lit.Next()));
                return arr;

            default:
                // Timestamp / GeoPoint / Blob / DocumentReference etc. — best-effort string form for v1.
                return JsonValue.Create(value.ToString());
        }
    }
}
#endif
