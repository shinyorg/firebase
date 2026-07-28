import Foundation
import FirebaseCore

// NOTE: this type must live in the SAME framework as the Messaging/Analytics shims.  The Firebase SPM
// products are static, so every framework that links them gets its own copy of FirebaseCore - configuring
// FIRApp from one framework and consuming it from another leaves the consumer's copy unconfigured and the
// completion blocks never fire.  Renamed off "FirebaseApplication" only to keep the Swift type from
// shadowing the imported Firebase module names now that all three shims share a module.
@objc(ShinyFirebaseApplication)
public class ShinyFirebaseApplication : NSObject {

    @objc
    public static func autoConfigure() -> Bool {
        guard !isConfigured() else { return true }
        FirebaseApp.configure()
        return isConfigured()
    }

    @objc(configure:gcmSenderId:apiKey:projectId:)
    public static func configure(googleAppId: String, gcmSenderId: String, apiKey: String?, projectId: String?) -> Bool {
        guard !isConfigured() else { return true }

        let opt = FirebaseOptions(googleAppID: googleAppId, gcmSenderID: gcmSenderId)
        opt.apiKey = apiKey
        opt.projectID = projectId
        FirebaseApp.configure(options: opt)

        return isConfigured()
    }

    @objc
    public static func isConfigured() -> Bool {
        return FirebaseApp.app() != nil
    }
}
