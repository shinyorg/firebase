using System;
using Foundation;

namespace Shiny.Firebase.iOS.Binding
{
    // The native types are prefixed (ShinyFirebase*) so the Swift shims do not shadow the Firebase module
    // names now that they share a single framework.  The managed names are kept unprefixed.
    [BaseType(typeof(NSObject), Name = "ShinyFirebaseApplication")]
    interface FirebaseApplication
    {
        [Static]
        [Export("autoConfigure")]
        bool AutoConfigure();

        [Static]
        [Export("configure:gcmSenderId:apiKey:projectId:")]
        bool Configure(string googleAppId, string gcmSenderId, [NullAllowed] string apiKey, [NullAllowed] string projectId);

        [Static]
        [Export("isConfigured")]
        bool IsConfigured { get; }
    }


    [BaseType(typeof(NSObject), Name = "ShinyFirebaseMessaging")]
    interface FirebaseMessaging
    {
        [Static]
        [Export("getIsAutoInitEnabled")]
        bool IsAutoInitEnabled { get; [Bind("setIsAutoInitEnabled:")] set; }

        [Static]
        [Export("getFcmToken")]
        [NullAllowed]
        string FcmToken { get; }

        [Static]
        [Export("register:completion:")]
        [Async]
        void Register(NSData nativePush, Action<string?, NSError?> completion);

        [Static]
        [Export("unregister:")]
        [Async]
        void UnRegister(Action<NSError?> completion);

        [Static]
        [Export("subscribe:completion:")]
        [Async]
        void Subscribe(string topic, Action<NSError?> completion);

        [Static]
        [Export("unsubscribe:completion:")]
        [Async]
        void UnSubscribe(string topic, Action<NSError?> completion);
    }


    [BaseType(typeof(NSObject), Name = "ShinyFirebaseAnalytics")]
    interface FirebaseAnalytics
    {
        [Static]
        [Export("logEventWithEventName:parameters:")]
        void LogEvent(string eventName, NSDictionary<NSString, NSObject> parameters);

        [Static]
        [Export("getAppInstanceIdWithCompletion:")]
        [Async]
        void GetAppInstanceId(Action<NSString> completion);

        [Static]
        [Export("setUserIdWithUserId:")]
        void SetUserId(string userId);

        [Static]
        [Export("setUserPropertyWithPropertyName:value:")]
        void SetUserProperty(string propertyName, string value);

        [Static]
        [Export("setSessionTimeoutWithSeconds:")]
        void SetSessionTimeout(nint seconds);

        [Static]
        [Export("resetAnalyticsData")]
        void ResetAnalyticsData();
    }
}
