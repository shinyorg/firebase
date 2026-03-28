# Shiny Firebase

[![NuGet](https://img.shields.io/nuget/v/Shiny.Push.FirebaseMessaging.svg?maxAge=2592000)](https://www.nuget.org/packages/Shiny.Push.FirebaseMessaging/)

Firebase Cloud Messaging (FCM) push notification support for .NET MAUI applications on iOS and Android, built on the [Shiny](https://shinylib.net) framework.

## Features

- Firebase Cloud Messaging for iOS and Android
- Embedded configuration via `GoogleService-Info.plist` / `google-services.json`
- Manual configuration support
- Topic subscription support (iOS)
- Custom push delegate for handling notification events
- Native iOS Firebase SDK 12.x via Slim Bindings

## Installation

```bash
dotnet add package Shiny.Push.FirebaseMessaging
```

## Setup

### MauiProgram.cs

```csharp
using Shiny;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShiny();

        // Use embedded configuration (GoogleService-Info.plist / google-services.json)
        builder.Services.AddPushFirebaseMessaging();

        // OR manual configuration
        builder.Services.AddPushFirebaseMessaging(new FirebaseConfiguration(
            UseEmbeddedConfiguration: false,
            AppId: "your-app-id",
            SenderId: "your-sender-id",
            ProjectId: "your-project-id",
            ApiKey: "your-api-key"
        ));

        // With a custom push delegate
        builder.Services.AddPushFirebaseMessaging<MyPushDelegate>();

        return builder.Build();
    }
}
```

## Platform Configuration

### iOS

1. Download `GoogleService-Info.plist` from the [Firebase Console](https://console.firebase.google.com)
2. Add to your iOS project with Build Action set to `BundleResource`
3. Enable **Push Notifications** capability in Entitlements.plist
4. Enable **Remote Notifications** background mode

### Android

1. Download `google-services.json` from the [Firebase Console](https://console.firebase.google.com)
2. Add to your Android project root

## Configuration Options

| Parameter | Type | Default | Description |
|---|---|---|---|
| `UseEmbeddedConfiguration` | `bool` | `true` | Use platform config files |
| `AppId` | `string?` | `null` | Firebase App ID |
| `SenderId` | `string?` | `null` | Firebase Sender ID |
| `ProjectId` | `string?` | `null` | Firebase Project ID |
| `ApiKey` | `string?` | `null` | Firebase API Key |
| `DefaultChannel` | `NotificationChannel?` | `null` | Android only - default notification channel |
| `IntentAction` | `string?` | `null` | Android only - custom intent action |

## Topic Subscriptions

The iOS implementation supports FCM topic subscriptions through `IPushTagSupport`:

```csharp
if (push is IPushTagSupport tagSupport)
{
    await tagSupport.AddTag("news");
    await tagSupport.RemoveTag("promotions");
    await tagSupport.SetTags("news", "updates");
    await tagSupport.ClearTags();
}
```

## License

MIT
