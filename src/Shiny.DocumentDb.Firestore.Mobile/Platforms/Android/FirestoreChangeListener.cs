#if ANDROID
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Android.Runtime;
using Com.Google.Firebase.Firestore;
using Object = Java.Lang.Object;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Bridges a native Firestore query snapshot listener to a managed <see cref="DocumentChange{T}"/> callback.
/// The first snapshot (the current result set, delivered as all-Added) is skipped so subscribers see only
/// changes that occur after they start observing — matching the IObservableDocumentStore / IChangeFeedDocumentStore
/// contract and the server Firestore provider's behavior.
/// </summary>
sealed class FirestoreChangeListener<T> : Java.Lang.Object, IEventListener where T : class
{
    readonly Func<DocumentChange<T>, Task> onChange;
    readonly JsonTypeInfo<T>? typeInfo;
    readonly System.Text.Json.JsonSerializerOptions? jsonOptions;
    bool first = true;

    public FirestoreChangeListener(
        Func<DocumentChange<T>, Task> onChange,
        JsonTypeInfo<T>? typeInfo,
        System.Text.Json.JsonSerializerOptions? jsonOptions)
    {
        this.onChange = onChange;
        this.typeInfo = typeInfo;
        this.jsonOptions = jsonOptions;
    }

    public void OnEvent(Object? value, FirebaseFirestoreException? error)
    {
        if (error != null || value == null)
            return;

        if (this.first)
        {
            this.first = false; // skip the initial current-state snapshot
            return;
        }

        var snapshot = value.JavaCast<QuerySnapshot>();
        foreach (var change in snapshot.DocumentChanges)
        {
            var mapped = this.Map(change);
            if (mapped != null)
                _ = this.onChange(mapped); // sequential delivery is the caller's concern; fire in order
        }
    }

    DocumentChange<T>? Map(Com.Google.Firebase.Firestore.DocumentChange change)
    {
        var doc = change.Document;
        var id = doc.Id;
        var type = change.GetType();

        if (type == Com.Google.Firebase.Firestore.DocumentChange.Type.Removed)
            return new DocumentChange<T> { ChangeType = DocumentChangeType.Removed, Id = id };

        var changeType = type == Com.Google.Firebase.Firestore.DocumentChange.Type.Added
            ? DocumentChangeType.Inserted
            : DocumentChangeType.Updated;

        T? document = null;
        var data = doc.Data;
        if (data != null)
        {
            var json = FirestoreValueConverter.ToJsonObject(data);
            document = FirestoreJson.Deserialize(json, this.typeInfo, this.jsonOptions);
        }
        return new DocumentChange<T> { ChangeType = changeType, Id = id, Document = document };
    }
}
#endif
