# Shiny.Firebase.Firestore.iOS.Binding

First-party .NET-for-iOS binding over the **official** Firebase Firestore SDK.
**Not** `Plugin.Firebase` or any third-party binding — see `plans/mobile-firestore-provider.md`.

Status: **skeleton** — `.csproj` items are commented out until the Xcode wrapper project and
`ApiDefinitions.cs` exist. Not yet referenced by `Shiny.DocumentDb.Firestore.Mobile`.

## Approach: Slim Binding (not a direct SDK bind)

This repo does **not** bind the Firebase Objective-C SDK surface directly. Every Firebase iOS binding here
(`Shiny.Firebase.Analytics.iOS.Binding`, `Shiny.Firebase.Messaging.iOS.Binding`) follows the same **Slim
Binding** pattern, and Firestore must too:

1. A small **Swift wrapper** in an Xcode *framework* project under `firebase_ios/native/<name>/`, exposing an
   `@objc` surface containing **only what the managed adapter needs**.
2. The Xcode project pulls Firebase via an **SPM package reference** to `firebase-ios-sdk`
   (`XCRemoteSwiftPackageReference`, currently pinned `12.15.0`, `upToNextMajorVersion`).
3. `firebase_ios/Firebase-ios.targets` runs `xcodebuild archive` for iOS + iOS Simulator + Mac Catalyst and
   combines the results into a single `.xcframework` under `<project>/.build/`.
4. `ApiDefinitions.cs` binds **that slim wrapper** — tens of lines, not thousands. `ObjSharpieBind=False`.

> **Why this matters.** An earlier draft of this file prescribed the opposite: download ~13 raw Firebase
> `.xcframework`s into `NativeLibs/`, run **Objective Sharpie** against the umbrella headers, and hand-clean a
> binding of the full `FIRFirestore` Objective-C surface. That is what made this "the heavy one" and "the
> highest-risk work item in the whole provider". **Do not do that.** It fights the repo's proven pattern and
> re-solves problems SPM already solves:
>
> - **The transitive native graph is not our problem.** SPM resolves it inside Xcode. Firestore's heavy
>   dependencies (`abseil-cpp-binary`, `grpc-binary`) ship as **prebuilt binary** SPM targets — they are
>   already pinned in the sibling bindings' `Package.resolved`. There is no `NativeLibs/` folder and no
>   hand-wired `<NativeReference>` list per framework; there is exactly **one** `NativeReference` — our own
>   built xcframework.
> - **Objective Sharpie is not needed and is not installed.** The working bindings set `ObjSharpieBind=False`
>   and hand-author a small `ApiDefinitions.cs` against the wrapper's `@objc` surface. Binding a slim Swift
>   surface we control is far smaller and more stable than binding Google's.

## Layout to create

```
firebase_ios/native/firestore/
  ShinyFirebaseFirestore.xcodeproj/
    project.pbxproj                     # framework target + SPM ref to firebase-ios-sdk (product: FirebaseFirestore)
    project.xcworkspace/…
  ShinyFirebaseFirestore/
    ShinyFirebaseFirestore.h            # umbrella header (Public)
    Firestore.swift                     # the @objc slim surface

src/Shiny.Firebase.Firestore.iOS.Binding/
  ApiDefinitions.cs                     # binds the slim surface
  Shiny.Firebase.Firestore.iOS.Binding.csproj   # reaches firebase_ios via ../../ — it lives under src/
```

Crib `firebase_ios/native/messaging/` — it is the reference implementation of every piece above.

## Steps to build

1. Create the Xcode framework project + Swift wrapper (mirror `native/messaging/`). Add the SPM package
   reference to `firebase-ios-sdk` and the `FirebaseFirestore` product dependency.
2. Author the `@objc` wrapper surface the adapter needs — mirror what the Android binding gives
   `Platforms/Android/`: configure/emulator/persistence settings, document get/set/delete, collection reads,
   query (where/order/limit) + execute, snapshot listeners + registration removal.
3. Author `ApiDefinitions.cs` against that surface.
4. Point the `.csproj` at the Xcode project and import `Firebase-ios.targets` (copy the Messaging csproj):
   `<XcodeProject>`, `<ObjSharpieBind>False`, one `<NativeReference>` to the built `.xcframework`,
   `NoBindingEmbedding=true`.
5. `dotnet build -f net10.0-ios` and iterate.
6. Reference it from `Shiny.DocumentDb.Firestore.Mobile` under a `net10.0-ios` condition, add the
   `net10.0-ios` TFM, and implement `Platforms/iOS/` mirroring the Android partials.

## Watch-outs

- **Marshalling.** Prefer passing documents across the boundary as **JSON strings** rather than
  `NSDictionary` of arbitrary types — it keeps `ApiDefinitions` tiny and matches how the managed side already
  serializes. Note the tradeoff: native Firestore types (`Timestamp`, `GeoPoint`, `DocumentReference`) do not
  round-trip through JSON.
- **App size.** The graph is large; measure the `.ipa` delta and document it. `SmartLink` + full linking help.
- **Reproducibility.** The SPM pin (`Package.resolved`) is the reproducibility mechanism — keep it committed.
- **Config.** The app ships `GoogleService-Info.plist`; `FirebaseApp.configure()` reads it. Explicit init from
  `MobileFirestoreOptions` is the fallback.
