import Foundation
import Combine
import FirebaseCore
import FirebaseMessaging
import FirebaseInstallations
import UIKit

// Renamed off "FirebaseMessaging" so the Swift type does not shadow the imported FirebaseMessaging module -
// this shim now shares a module with ShinyFirebaseApplication so that both talk to the same FIRApp.
@objc(ShinyFirebaseMessaging)
public class ShinyFirebaseMessaging : NSObject {

    static let errorDomain = "ShinyFirebaseMessaging"

    // Messaging.messaging() traps when the default FirebaseApp has not been configured.  Surface that as an
    // error rather than letting the caller await a completion block that will never fire.
    static func notConfigured() -> NSError {
        return NSError(
            domain: errorDomain,
            code: 1,
            userInfo: [NSLocalizedDescriptionKey: "Firebase has not been configured - call ShinyFirebaseApplication.configure first"]
        )
    }

    static var isConfigured: Bool { FirebaseApp.app() != nil }

    @objc(setIsAutoInitEnabled:)
    public static func setIsAutoInitEnabled(enabled: Bool) {
        guard isConfigured else { return }
        Messaging.messaging().isAutoInitEnabled = enabled
    }

    @objc
    public static func getIsAutoInitEnabled() -> Bool {
        guard isConfigured else { return false }
        return Messaging.messaging().isAutoInitEnabled
    }

    @objc(register:completion:)
    public static func register(apnsToken: NSData, completion: @escaping (String?, NSError?) -> Void) {
        guard isConfigured else {
            completion(nil, notConfigured())
            return
        }
        let data = Data(referencing: apnsToken);
        Messaging.messaging().apnsToken = data

        Messaging.messaging().token(completion: { fid, error in
            completion(fid, error as NSError?)
        })
    }

    @objc(unregister:)
    public static func unregister(completion: @escaping (NSError?) -> Void) {
        guard isConfigured else {
            completion(notConfigured())
            return
        }
        // need delegate to watch for fcmToken updates
        Messaging.messaging().deleteToken(completion: { error in
            completion(error as NSError?)
        })
    }

    @objc
    public static func getFcmToken() -> String? {
        guard isConfigured else { return nil }
        return Messaging.messaging().fcmToken
    }

    @objc(subscribe:completion:)
    public static func subscribe(topic: String, completion: @escaping (NSError?) -> Void) {
        guard isConfigured else {
            completion(notConfigured())
            return
        }
        Messaging.messaging().subscribe(toTopic: topic, completion: { error in
            completion(error as NSError?)
        })
    }

    @objc(unsubscribe:completion:)
    public static func unsubscribe(topic: String, completion: @escaping (NSError?) -> Void) {
        guard isConfigured else {
            completion(notConfigured())
            return
        }
        Messaging.messaging().unsubscribe(fromTopic: topic, completion: { error in
            completion(error as NSError?)
        })
    }
}
