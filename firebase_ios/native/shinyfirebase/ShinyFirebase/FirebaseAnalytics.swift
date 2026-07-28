import Foundation
import FirebaseAnalytics

// Renamed off "FirebaseAnalytics" so the Swift type does not shadow the imported FirebaseAnalytics module -
// this shim now shares a module with ShinyFirebaseApplication so that both talk to the same FIRApp.
@objc(ShinyFirebaseAnalytics)
public class ShinyFirebaseAnalytics : NSObject {

    @objc
    public static func logEvent(eventName: String, parameters: Dictionary<String, Any>) {
        Analytics.logEvent(eventName, parameters: parameters)
    }

    @objc
    public static func getAppInstanceId(completion: @escaping (String?) -> Void) {
        completion(Analytics.appInstanceID())
    }

    @objc
    public static func setUserId(userId: String) {
        Analytics.setUserID(userId)
    }

    @objc
    public static func setUserProperty(propertyName: String, value: String) {
        Analytics.setUserProperty(value, forName: propertyName)
    }

    @objc
    public static func setSessionTimeout(seconds: Int) {
        Analytics.setSessionTimeoutInterval(TimeInterval(seconds))
    }

    @objc
    public static func resetAnalyticsData() {
        Analytics.resetAnalyticsData()
    }
}
