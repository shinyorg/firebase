#if IOS
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using Native = Shiny.Firebase.Firestore.iOS.Binding;

namespace Shiny.DocumentDb.Firestore.Mobile;

// Real-time change feed backed by native Firestore snapshot listeners. The Swift wrapper skips the initial
// snapshot and hands back (changeType, documentId, documentJson) — matching the Android head's behavior.
public partial class MobileFirestoreDocumentStore
{
    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeChanges<T>(
        Func<DocumentChange<T>, CancellationToken, Task> onChange,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(onChange);

        var listener = Native.Firestore
            .Query(this.ResolveCollection<T>())
            .AddSnapshotListener((type, id, json) =>
            {
                var change = this.ToChange<T>(type, id, json?.ToString(), null);
                if (change != null)
                    _ = onChange(change, cancellationToken);
            });

        return Task.FromResult<IAsyncDisposable>(new ListenerHandle(listener));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DocumentChange<T>> NotifyOnChange<T>(CancellationToken cancellationToken = default) where T : class
        => this.ListenQuery<T>(Native.Firestore.Query(this.ResolveCollection<T>()), null, cancellationToken);

    // Shared listener→IAsyncEnumerable pump over any native query (collection- or filtered-query-scoped).
    internal async IAsyncEnumerable<DocumentChange<T>> ListenQuery<T>(
        Native.FirestoreQuery query,
        JsonTypeInfo<T>? typeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var channel = Channel.CreateUnbounded<DocumentChange<T>>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var listener = query.AddSnapshotListener((type, id, json) =>
        {
            var change = this.ToChange<T>(type, id, json?.ToString(), typeInfo);
            if (change != null)
                channel.Writer.TryWrite(change);
        });

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            listener.Remove();
            channel.Writer.TryComplete();
        }
    }

    DocumentChange<T>? ToChange<T>(string type, string id, string? json, JsonTypeInfo<T>? typeInfo) where T : class
    {
        var changeType = type switch
        {
            "added" => DocumentChangeType.Inserted,
            "modified" => DocumentChangeType.Updated,
            "removed" => DocumentChangeType.Removed,
            _ => (DocumentChangeType?)null
        };
        if (changeType == null)
            return null;

        // Firestore still carries last-known data on a removal, but the Android head reports Removed with no
        // Document — keep the platforms identical rather than leaking a per-platform difference to callers.
        if (changeType == DocumentChangeType.Removed)
            return new DocumentChange<T> { Id = id, ChangeType = DocumentChangeType.Removed };

        return new DocumentChange<T>
        {
            Id = id,
            ChangeType = changeType.Value,
            Document = string.IsNullOrEmpty(json) ? null : this.Deserialize<T>(json, typeInfo)
        };
    }

    sealed class ListenerHandle(Native.FirestoreListener listener) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            listener.Remove();
            return ValueTask.CompletedTask;
        }
    }
}
#endif
