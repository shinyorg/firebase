using Shiny.DocumentDb.Internal;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// On-device Firebase Firestore document store. The native Firebase SDK owns persistence, the offline write
/// queue, real-time listeners, and conflict handling; this class is the thin adapter that maps
/// <see cref="IDocumentStore"/> operations onto native Firestore calls and translates documents to/from the
/// native field map.
/// </summary>
/// <remarks>
/// The real implementation lives in the platform partial (<c>MobileFirestoreDocumentStore.Android.cs</c>,
/// compiled for <c>net10.0-android</c>). On the plain <c>net10.0</c> build the stub partial
/// (<c>MobileFirestoreDocumentStore.Stub.cs</c>) throws <see cref="PlatformNotSupportedException"/>.
/// </remarks>
public partial class MobileFirestoreDocumentStore : DocumentProviderBase, IDocumentStore, IDocumentMaintenance, IObservableDocumentStore, IChangeFeedDocumentStore
{
    readonly MobileFirestoreOptions options;
    readonly IServiceProvider services;

    public MobileFirestoreDocumentStore(MobileFirestoreOptions options, IServiceProvider services)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.services = services;
        this.InitializePlatform();
    }

    protected override InterceptorPipeline Interceptors => this.options.Interceptors;

    // Implemented per-platform: Android configures the native FirebaseFirestore instance; the stub no-ops.
    partial void InitializePlatform();

    // Resolve the Firestore collection name for a document type (respects MapTypeToCollection).
    string ResolveCollection<T>() => this.options.ResolveCollectionName(typeof(T), typeof(T).Name);
}
