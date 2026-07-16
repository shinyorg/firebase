#if ANDROID
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Com.Google.Firebase.Firestore;

namespace Shiny.DocumentDb.Firestore.Mobile;

// Real-time change feed backed by native Firestore query snapshot listeners.
public partial class MobileFirestoreDocumentStore
{
    /// <inheritdoc />
    public Task<IAsyncDisposable> SubscribeChanges<T>(
        Func<DocumentChange<T>, CancellationToken, Task> onChange,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(onChange);
        var listener = new FirestoreChangeListener<T>(
            c => onChange(c, cancellationToken),
            null,
            this.options.JsonSerializerOptions);

        var registration = this.Collection<T>().AddSnapshotListener(listener);
        return Task.FromResult<IAsyncDisposable>(new ListenerHandle(registration));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DocumentChange<T>> NotifyOnChange<T>(CancellationToken cancellationToken = default) where T : class
        => this.ListenQuery<T>(this.Collection<T>(), null, cancellationToken);

    // Shared listener→IAsyncEnumerable pump over any native Query (collection- or filtered-query-scoped).
    internal async IAsyncEnumerable<DocumentChange<T>> ListenQuery<T>(
        Query query,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? typeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var channel = Channel.CreateUnbounded<DocumentChange<T>>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var listener = new FirestoreChangeListener<T>(
            c => { channel.Writer.TryWrite(c); return Task.CompletedTask; },
            typeInfo,
            this.options.JsonSerializerOptions);

        var registration = query.AddSnapshotListener(listener);
        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            registration.Remove();
            channel.Writer.TryComplete();
        }
    }

    sealed class ListenerHandle(IListenerRegistration registration) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            registration.Remove();
            return ValueTask.CompletedTask;
        }
    }
}
#endif
