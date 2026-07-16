# Shiny Firebase

[![NuGet](https://img.shields.io/nuget/v/Shiny.Push.FirebaseMessaging.svg?maxAge=2592000)](https://www.nuget.org/packages/Shiny.Push.FirebaseMessaging/)

Firebase for .NET MAUI, built on the [Shiny](https://shinylib.net) framework — Cloud Messaging push
notifications and an on-device Firestore document store, over **first-party Shiny bindings** to the native
Firebase SDKs

## Packages

| Package | What it does | Platforms |
|---|---|---|
| `Shiny.Push.FirebaseMessaging` | Firebase Cloud Messaging (FCM) push notifications | iOS, Android |
| `Shiny.DocumentDb.Firestore.Mobile` | On-device Firestore document store for Shiny.DocumentDb — offline-first, real-time | iOS, Android |

---

# Firebase Cloud Messaging

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

---

# Mobile Firestore

[![NuGet](https://img.shields.io/nuget/v/Shiny.DocumentDb.Firestore.Mobile.svg?maxAge=2592000)](https://www.nuget.org/packages/Shiny.DocumentDb.Firestore.Mobile/)

An **on-device** Firebase Firestore provider for [Shiny.DocumentDb](https://shinylib.net). The native Firebase
SDK owns persistence, the offline write queue, real-time listeners, and conflict handling — this package is
the typed adapter that maps `IDocumentStore` onto it.

This is not `Shiny.DocumentDb.Firestore`, which drives Firestore from a **server** via the admin SDK and a
service account. This one runs **on the device**, under the end user's Firebase identity, and works offline.

## Features

- Offline-first — native persistent cache, writes queue and drain on reconnect
- Real-time change feed over native snapshot listeners
- LINQ queries pushed down to the native Firestore query (filter, order, limit)
- Managed Firebase Auth identity (anonymous + email/password, token refresh)
- Firestore emulator support

## Platform support

**Android and iOS**, over first-party Shiny bindings to the native Firebase SDKs. Both heads are verified
end-to-end against the Firestore emulator (CRUD, real-time, query, identity). The `net10.0` target is a
throw-stub (`PlatformNotSupportedException`) so the surface stays unit-testable without a device.

## Installation

```bash
dotnet add package Shiny.DocumentDb.Firestore.Mobile
```

## Setup

Bundle `google-services.json` in your Android project (Firebase auto-init), or call
`FirebaseApp.InitializeApp` before the store resolves.

```csharp
using Shiny.DocumentDb;

builder.Services.AddMobileFirestoreDocumentStore(o =>
{
    o.ProjectId = "my-project";           // optional when google-services.json is bundled
    o.PersistenceEnabled = true;          // default — offline cache on
    o.MapTypeToCollection<Play>("plays"); // default collection = type name
});

// Optional — managed Firebase Auth (REST)
builder.Services.AddFirebaseIdentity(o => o.ApiKey = "your-web-api-key");
```

## Usage

```csharp
var store = sp.GetRequiredService<IDocumentStore>();

await store.Insert(new Play { Id = "p1", Name = "Slant Left", Version = 1 });
var play = await store.Get<Play>("p1");
await store.Remove<Play>("p1");

// LINQ — with an inequality filter, order by the inequality field first (a Firestore rule)
var plays = await store
    .Query<Play>()
    .Where(p => p.Version >= 2)
    .OrderBy(p => p.Version)
    .ToList();

// Real-time — the initial snapshot is skipped; you get changes from subscription onward
await foreach (var change in store.NotifyOnChange<Play>(ct))
    Console.WriteLine($"{change.ChangeType}: {change.Id}");
```

The document id is the Firestore document id, taken from an `Id` property by default
(`MapIdProperty<T>` to override). It must be set on every write — the provider does not generate ids.

## Current limitations

The provider is shipping in milestones. Today:

- **`Insert` is an upsert.** `Insert`/`Update`/`Upsert` all map to native `set()`; there is no
  insert-if-absent yet.
- **Write interceptors and `MapVersionProperty` are inert** — they configure but do not run, so optimistic
  concurrency is not enforced. Keep that logic in calling code for now.
- **Security rules are not yet per-user.** `IFirebaseIdentity` obtains and refreshes tokens in managed code,
  but the token is not yet flowed into the native SDK's request auth, so rules do not see `request.auth.uid`
  for native traffic. Scope per-user data by collection path
  (`MapTypeToCollection<T>($"users/{uid}/plays")`) until the native auth binding lands.
- These throw `NotSupportedException`: the string-`WHERE` query overloads, `BatchInsert`, `SetProperty`,
  `RemoveProperty`, `GetDiff`, `ClearAll`, and `Select` projection.
- `Count` and the aggregates materialize documents rather than using native aggregates.

## License

MIT
