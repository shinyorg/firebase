using System;
using Foundation;
using ObjCRuntime;

// Binds the slim @objc surface exposed by firebase_ios/native/firestore/ShinyFirebaseFirestore.
// Selectors here mirror ShinyFirebaseFirestore-Swift.h exactly (the header Xcode generates from the Swift
// wrapper) — regenerate/compare against it after changing the Swift. Documents cross as JSON strings.

namespace Shiny.Firebase.Firestore.iOS.Binding
{
    /// <summary>A live snapshot listener registration. Call <see cref="Remove"/> to detach.</summary>
    [BaseType(typeof(NSObject), Name = "ShinyFirestoreListener")]
    [DisableDefaultCtor]
    interface FirestoreListener
    {
        [Export("remove")]
        void Remove();
    }

    /// <summary>A native Firestore query builder. Each builder method mutates and returns the same instance.</summary>
    [BaseType(typeof(NSObject), Name = "ShinyFirestoreQuery")]
    [DisableDefaultCtor]
    interface FirestoreQuery
    {
        /// <param name="field">The Firestore field name (the serialized JSON property name).</param>
        /// <param name="op">One of "==", "!=", "&lt;", "&lt;=", "&gt;", "&gt;=", "in", "array-contains".</param>
        /// <param name="jsonValue">The comparison value as a JSON scalar (or array for "in").</param>
        [Export("whereField:op:jsonValue:")]
        FirestoreQuery WhereField(string field, string op, string jsonValue);

        [Export("orderBy:descending:")]
        FirestoreQuery OrderBy(string field, bool descending);

        [Export("limitTo:")]
        FirestoreQuery LimitTo(nint count);

        /// <summary>Completion receives a JSON array of <c>{ "id": …, "data": { … } }</c>.</summary>
        [Export("getDocuments:")]
        [Async]
        void GetDocuments(Action<NSString, NSError> completion);

        /// <summary>
        /// Streams changes. The callback receives (changeType, documentId, documentJson) where changeType is
        /// "added" | "modified" | "removed". The initial snapshot is skipped, matching the Android adapter.
        /// </summary>
        [Export("addSnapshotListener:")]
        FirestoreListener AddSnapshotListener(Action<NSString, NSString, NSString> onChange);
    }

    /// <summary>Static entry point onto the native Firestore SDK.</summary>
    [BaseType(typeof(NSObject), Name = "ShinyFirestore")]
    interface Firestore
    {
        /// <summary>Explicit FirebaseApp init. No-op when already configured (a bundled plist wins).</summary>
        [Static]
        [Export("configureWithProjectId:appId:apiKey:")]
        void Configure(string projectId, string appId, string apiKey);

        /// <summary>Configures from a bundled GoogleService-Info.plist. No-op when already configured.</summary>
        [Static]
        [Export("configureDefault")]
        void ConfigureDefault();

        [Static]
        [Export("isConfigured")]
        bool IsConfigured();

        /// <summary>Must be called before any operation — the native SDK forbids mutating settings after use.</summary>
        [Static]
        [Export("applySettingsWithPersistenceEnabled:emulatorHost:emulatorPort:")]
        void ApplySettings(bool persistenceEnabled, [NullAllowed] string emulatorHost, nint emulatorPort);

        /// <summary>Firestore set() — an upsert.</summary>
        [Static]
        [Export("setDocumentInCollection:documentId:json:completion:")]
        [Async]
        void SetDocument(string collection, string documentId, string json, Action<NSError> completion);

        /// <summary>Completion receives the document JSON, or null when the document does not exist.</summary>
        [Static]
        [Export("getDocumentInCollection:documentId:completion:")]
        [Async]
        void GetDocument(string collection, string documentId, Action<NSString, NSError> completion);

        [Static]
        [Export("deleteDocumentInCollection:documentId:completion:")]
        [Async]
        void DeleteDocument(string collection, string documentId, Action<NSError> completion);

        [Static]
        [Export("updateFieldInCollection:documentId:field:jsonValue:completion:")]
        [Async]
        void UpdateField(string collection, string documentId, string field, string jsonValue, Action<NSError> completion);

        [Static]
        [Export("queryForCollection:")]
        FirestoreQuery Query(string collection);
    }
}
