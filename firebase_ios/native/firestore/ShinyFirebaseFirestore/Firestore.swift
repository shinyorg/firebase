import Foundation
import FirebaseCore
import FirebaseFirestore

// Slim @objc surface over the native Firestore SDK — only what Shiny.DocumentDb.Firestore.Mobile's adapter
// needs. Documents cross the boundary as JSON strings (not NSDictionary) so ApiDefinitions stays small and
// the managed side keeps using System.Text.Json. See ../../../Shiny.Firebase.Firestore.iOS.Binding/README.md.

// MARK: - JSON <-> Firestore field map

enum ShinyFirestoreJson {

    /// Firestore hands back types JSONSerialization refuses (Timestamp, GeoPoint, DocumentReference).
    /// Normalize them to JSON-safe values. Mirrors the Android FirestoreValueConverter's constraints.
    static func sanitize(_ value: Any) -> Any {
        switch value {
        case let timestamp as Timestamp:
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            return formatter.string(from: timestamp.dateValue())
        case let geo as GeoPoint:
            return ["latitude": geo.latitude, "longitude": geo.longitude]
        case let ref as DocumentReference:
            return ref.path
        case let data as Data:
            return data.base64EncodedString()
        case let dict as [String: Any]:
            return dict.mapValues { sanitize($0) }
        case let array as [Any]:
            return array.map { sanitize($0) }
        default:
            return value
        }
    }

    static func toJson(_ map: [String: Any]) -> String? {
        let safe = map.mapValues { sanitize($0) }
        guard JSONSerialization.isValidJSONObject(safe),
              let data = try? JSONSerialization.data(withJSONObject: safe)
        else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func toMap(_ json: String) -> [String: Any]? {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data),
              let map = obj as? [String: Any]
        else { return nil }
        return map
    }

    /// Decodes a single JSON scalar (used for query comparison values).
    static func toValue(_ json: String) -> Any? {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
        else { return nil }
        return obj is NSNull ? nil : obj
    }

    static func error(_ message: String) -> NSError {
        NSError(domain: "ShinyFirebaseFirestore", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}

// MARK: - Listener handle

@objc(ShinyFirestoreListener)
public class ShinyFirestoreListener: NSObject {
    private var registration: ListenerRegistration?

    init(_ registration: ListenerRegistration) {
        self.registration = registration
    }

    @objc
    public func remove() {
        self.registration?.remove()
        self.registration = nil
    }
}

// MARK: - Query

@objc(ShinyFirestoreQuery)
public class ShinyFirestoreQuery: NSObject {
    private var query: Query

    init(_ query: Query) {
        self.query = query
    }

    /// op: one of "==", "!=", "<", "<=", ">", ">=", "in", "array-contains".
    /// value is a JSON scalar/array so the managed translator can pass any primitive without extra bindings.
    @objc(whereField:op:jsonValue:)
    public func whereField(_ field: String, op: String, jsonValue: String) -> ShinyFirestoreQuery {
        let value = ShinyFirestoreJson.toValue(jsonValue) as Any
        switch op {
        case "==": self.query = self.query.whereField(field, isEqualTo: value)
        case "!=": self.query = self.query.whereField(field, isNotEqualTo: value)
        case "<": self.query = self.query.whereField(field, isLessThan: value)
        case "<=": self.query = self.query.whereField(field, isLessThanOrEqualTo: value)
        case ">": self.query = self.query.whereField(field, isGreaterThan: value)
        case ">=": self.query = self.query.whereField(field, isGreaterThanOrEqualTo: value)
        case "array-contains": self.query = self.query.whereField(field, arrayContains: value)
        case "in": self.query = self.query.whereField(field, in: (value as? [Any]) ?? [])
        default: break
        }
        return self
    }

    @objc(orderBy:descending:)
    public func orderBy(_ field: String, descending: Bool) -> ShinyFirestoreQuery {
        self.query = self.query.order(by: field, descending: descending)
        return self
    }

    @objc(limitTo:)
    public func limitTo(_ count: Int) -> ShinyFirestoreQuery {
        self.query = self.query.limit(to: count)
        return self
    }

    /// Completion receives a JSON array of `{ "id": …, "data": { … } }`.
    @objc(getDocuments:)
    public func getDocuments(completion: @escaping (String?, NSError?) -> Void) {
        self.query.getDocuments { snapshot, error in
            if let error = error {
                completion(nil, error as NSError)
                return
            }
            completion(ShinyFirestore.encode(snapshot?.documents ?? []), nil)
        }
    }

    /// Streams changes. `onChange` receives (changeType, documentId, documentJson).
    /// changeType is "added" | "modified" | "removed". The initial snapshot is skipped so callers see only
    /// changes from subscription onward — matching the Android adapter's behavior.
    @objc(addSnapshotListener:)
    public func addSnapshotListener(onChange: @escaping (String, String, String?) -> Void) -> ShinyFirestoreListener {
        var seenInitial = false
        let registration = self.query.addSnapshotListener { snapshot, _ in
            guard let snapshot = snapshot else { return }
            if !seenInitial {
                seenInitial = true
                return
            }
            for change in snapshot.documentChanges {
                let type: String
                switch change.type {
                case .added: type = "added"
                case .modified: type = "modified"
                case .removed: type = "removed"
                }
                let json = ShinyFirestoreJson.toJson(change.document.data())
                onChange(type, change.document.documentID, json)
            }
        }
        return ShinyFirestoreListener(registration)
    }
}

// MARK: - Firestore

@objc(ShinyFirestore)
public class ShinyFirestore: NSObject {

    private static var emulatorHost: String?
    private static var emulatorPort: Int = 8080
    private static var persistenceEnabled = true

    private static var db: Firestore {
        Firestore.firestore()
    }

    /// Configures FirebaseApp explicitly. Skipped when GoogleService-Info.plist is bundled and already
    /// configured — the app's own configure() wins.
    @objc(configureWithProjectId:appId:apiKey:)
    public static func configure(projectId: String, appId: String, apiKey: String) {
        guard FirebaseApp.app() == nil else { return }
        let options = FirebaseOptions(googleAppID: appId, gcmSenderID: "")
        options.projectID = projectId
        options.apiKey = apiKey
        FirebaseApp.configure(options: options)
    }

    /// Configures from a bundled GoogleService-Info.plist. No-op when already configured.
    @objc
    public static func configureDefault() {
        guard FirebaseApp.app() == nil else { return }
        FirebaseApp.configure()
    }

    @objc
    public static func isConfigured() -> Bool {
        FirebaseApp.app() != nil
    }

    /// Applies settings. Must be called before any Firestore operation — the native SDK forbids mutating
    /// settings after use.
    @objc(applySettingsWithPersistenceEnabled:emulatorHost:emulatorPort:)
    public static func applySettings(persistenceEnabled: Bool, emulatorHost: String?, emulatorPort: Int) {
        self.persistenceEnabled = persistenceEnabled
        self.emulatorHost = emulatorHost
        self.emulatorPort = emulatorPort

        let db = self.db
        let settings = db.settings
        settings.cacheSettings = persistenceEnabled
            ? PersistentCacheSettings()
            : MemoryCacheSettings()

        if let host = emulatorHost, !host.isEmpty {
            settings.host = "\(host):\(emulatorPort)"
            settings.isSSLEnabled = false
        }
        db.settings = settings
    }

    static func encode(_ documents: [QueryDocumentSnapshot]) -> String? {
        let items: [[String: Any]] = documents.compactMap { doc in
            guard let data = ShinyFirestoreJson.toJson(doc.data()),
                  let map = ShinyFirestoreJson.toMap(data)
            else { return nil }
            return ["id": doc.documentID, "data": map]
        }
        guard let out = try? JSONSerialization.data(withJSONObject: items) else { return nil }
        return String(data: out, encoding: .utf8)
    }

    // MARK: CRUD

    /// Firestore set() — an upsert. Mirrors the Android adapter, where Insert/Update/Upsert all land here.
    @objc(setDocumentInCollection:documentId:json:completion:)
    public static func setDocument(
        collection: String,
        documentId: String,
        json: String,
        completion: @escaping (NSError?) -> Void
    ) {
        guard let map = ShinyFirestoreJson.toMap(json) else {
            completion(ShinyFirestoreJson.error("Document JSON is not a JSON object."))
            return
        }
        self.db.collection(collection).document(documentId).setData(map) { error in
            completion(error as NSError?)
        }
    }

    /// Completion receives the document JSON, or nil when the document does not exist.
    @objc(getDocumentInCollection:documentId:completion:)
    public static func getDocument(
        collection: String,
        documentId: String,
        completion: @escaping (String?, NSError?) -> Void
    ) {
        self.db.collection(collection).document(documentId).getDocument { snapshot, error in
            if let error = error {
                completion(nil, error as NSError)
                return
            }
            guard let snapshot = snapshot, snapshot.exists, let data = snapshot.data() else {
                completion(nil, nil)
                return
            }
            completion(ShinyFirestoreJson.toJson(data), nil)
        }
    }

    @objc(deleteDocumentInCollection:documentId:completion:)
    public static func deleteDocument(
        collection: String,
        documentId: String,
        completion: @escaping (NSError?) -> Void
    ) {
        self.db.collection(collection).document(documentId).delete { error in
            completion(error as NSError?)
        }
    }

    @objc(updateFieldInCollection:documentId:field:jsonValue:completion:)
    public static func updateField(
        collection: String,
        documentId: String,
        field: String,
        jsonValue: String,
        completion: @escaping (NSError?) -> Void
    ) {
        let value = ShinyFirestoreJson.toValue(jsonValue) as Any
        self.db.collection(collection).document(documentId).updateData([field: value]) { error in
            completion(error as NSError?)
        }
    }

    // MARK: Query

    @objc(queryForCollection:)
    public static func query(collection: String) -> ShinyFirestoreQuery {
        ShinyFirestoreQuery(self.db.collection(collection))
    }
}
