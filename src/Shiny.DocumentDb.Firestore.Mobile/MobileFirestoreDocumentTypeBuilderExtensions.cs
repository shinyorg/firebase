using System.Diagnostics.CodeAnalysis;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>Firestore's vocabulary for a document type's storage unit — the same thing <c>cfg.Table</c> sets.</summary>
public static class MobileFirestoreDocumentTypeBuilderExtensions
{
    /// <summary>
    /// Overrides the native Firestore collection this document type lives in. Defaults to the resolved type
    /// name (per the store's <see cref="TypeNameResolution"/>).
    /// </summary>
    /// <example>
    /// <code>options.ConfigureDocument&lt;Patient&gt;(cfg => cfg.ToCollection("patients"));</code>
    /// </example>
    public static DocumentTypeBuilder<T> ToCollection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this DocumentTypeBuilder<T> cfg, string collectionName) where T : class
    {
        ArgumentNullException.ThrowIfNull(cfg);
        cfg.Table = collectionName;
        return cfg;
    }

    /// <summary>
    /// Gives this document type a Firestore collection named after the type, per the store's
    /// <see cref="TypeNameResolution"/> — the default, stated explicitly.
    /// </summary>
    public static DocumentTypeBuilder<T> ToCollection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(this DocumentTypeBuilder<T> cfg) where T : class
    {
        ArgumentNullException.ThrowIfNull(cfg);
        cfg.Table = cfg.TypeName;
        return cfg;
    }
}
