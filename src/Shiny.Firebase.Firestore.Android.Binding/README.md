# Shiny.Firebase.Firestore.Android.Binding

First-party .NET-for-Android binding over the **official** Firebase Firestore + Auth AARs. **Not** a
third-party binding — see `plans/mobile-firestore-provider.md` (binding policy).

Status: **DONE — builds green, verified end-to-end on the emulator.** A device test app drove
Insert/Get/Upsert/Remove through this binding + the DocumentDb adapter against the Firestore emulator and
logged `overall: ALLPASS`. Not yet in `Firebase.slnx` (the android head is referenced directly by
`Shiny.DocumentDb.Firestore.Mobile`).

## Runtime dependency closure (the hard-won part)

`<AndroidMavenLibrary>` does **not** pull transitive deps, so Firestore's full runtime closure is listed
explicitly. Bound (`Bind="true"`): `firebase-firestore`, `firebase-common`, `play-services-tasks`. Classpath
only (`Bind="false"`): `play-services-basement`/`-base`, `firebase-components`/`-annotations`/`-encoders`/
`-encoders-json`/`-auth-interop`/`-appcheck-interop`, `protolite-well-known-types`,
`firebase-database-collection`, gRPC 1.62.2 (`grpc-android`/`-okhttp`/`-protobuf-lite`/`-stub`/`-core`/`-api`/
`-context`/`-util`), `protobuf-javalite`, `perfmark-api`, `re2j`. Via Xamarin NuGets (KMP/dedup-safe):
`Xamarin.AndroidX.Collection`/`Core`/`Annotation`/`Lifecycle.Process`/`DataStore.Preferences(.Core)`,
`Xamarin.Google.Guava` (full guava — replaces the Maven one to avoid the `ListenableFuture` duplicate; okio
comes transitively via DataStore).

Deferred: full `firebase-auth` (drags in recaptcha + datastore-heavy identity — identity is a later
milestone); `EventListener`/`AddSnapshotListener` (its generic Java bridge fails javac — real-time is a later
milestone).

## Native artifacts

Resolved from Google's Maven repo, pinned via the Firebase BOM (`com.google.firebase:firebase-bom`):

| Artifact | Purpose |
|---|---|
| `com.google.firebase:firebase-firestore` | Firestore client (persistence, listeners, transactions) |
| `com.google.firebase:firebase-auth` | Per-user identity → `request.auth.uid` for rules |
| `com.google.firebase:firebase-common` | `FirebaseApp` init |

Transitive: Play Services (`play-services-base`, `-tasks`), gRPC, `protobuf-javalite`, AndroidX. The
binding generator will report missing types — satisfy each with the matching `Xamarin.AndroidX.*` /
`Xamarin.Google*` binding NuGet or an additional `<AndroidMavenLibrary>`.

## Steps to build

1. Pin the BOM version; set the three `<AndroidMavenLibrary>` versions to match.
2. `dotnet build` → read the `BG86xx` "missing type" warnings.
3. Add `Xamarin.AndroidX.*` / `Xamarin.GooglePlayServices.*` NuGets until clean.
4. Add `Transforms/Metadata.xml` to hide internals and fix `Task<T>` / `EventListener<T>` generics.
5. Expose the minimal surface the adapter needs (see the parent provider's `Platforms/Android` adapter):
   `FirebaseFirestore`, `CollectionReference`/`Query`, `DocumentReference`, `SetOptions`,
   `DocumentSnapshot`/`QuerySnapshot`, `ListenerRegistration`, `Transaction`/`RunTransaction`,
   `FirebaseFirestoreSettings` (persistence), `UseEmulator`; plus `FirebaseAuth`, `FirebaseUser`.
6. Add to `Firebase.slnx` and reference from `Shiny.DocumentDb.Firestore.Mobile` under a
   `net10.0-android` condition.

## Concrete progress (2026-07)

Real build attempt done; the path is proven and characterized. What's established:

- **Versions** (Google Maven, `Repository="Google"` — Firebase is NOT on Maven Central):
  firestore **26.4.1**, auth **24.2.0**, common **22.1.0**.
- **Toolchain bug worked around:** the SDK's `_VerifyJavaDependencies` task crashes (`XAJDV7004`,
  null-version parse) on Firebase's BOM-managed transitive POMs. Bypassed by switching to explicit
  `Sdk.props`/`Sdk.targets` imports and defining an empty `_VerifyJavaDependencies` target *after* the
  import so it wins. (Gated on `$(BuildFirebaseBinding)`.)
- **Reached C# binding generation.** Current failures (11) are the classic "internal types leak" wall,
  all fixable with `Transforms/Metadata.xml`:
  - `CS0535` — `Comparable` types generated without `CompareTo`: `Blob`, `DatabaseId`, `DocumentKey`,
    `GeoPoint`, `SnapshotVersion`, `Timestamp`.
  - `CS0234` — internal impl type `Com.Google.Firebase.Firestore.Local.IReferenceDelegate` missing.
  - `CS0101` / `CS1715` — duplicate/naming collisions from internal packages.

### Exact next steps
1. Add `Transforms/Metadata.xml` that **removes the internal packages** (`.firestore.local`,
   `.firestore.model`, `.firestore.core`, `.firestore.remote`, `.firestore.util`) and keeps only the
   public surface: `FirebaseFirestore`, `CollectionReference`, `Query`, `DocumentReference`,
   `DocumentSnapshot`, `QuerySnapshot`, `DocumentChange`, `ListenerRegistration`, `Transaction`,
   `WriteBatch`, `FieldValue`, `FieldPath`, `Blob`, `GeoPoint`, `Timestamp`, `SetOptions`,
   `FirebaseFirestoreSettings`, `Source`, `MetadataChanges`; plus `FirebaseAuth`, `FirebaseUser`,
   `AuthResult`, `FirebaseApp`.
2. Re-run `dotnet build -p:BuildFirebaseBinding=true`; expect a second transitive layer
   (`play-services-base`/`-tasks`, `grpc`, `protobuf-javalite`, AndroidX) → satisfy each with the matching
   `Xamarin.AndroidX.*` / `Xamarin.GooglePlayServices.*` binding NuGet or `<AndroidMavenLibrary>`.
3. When green, remove the `$(BuildFirebaseBinding)` gate, add to `Firebase.slnx`, and reference from
   `Shiny.DocumentDb.Firestore.Mobile` under `net10.0-android`.

## Config

App must ship `google-services.json`; `FirebaseApp.InitializeApp(context)` reads it. Explicit init from
`MobileFirestoreOptions` (ProjectId/AppId/ApiKey) is the fallback when no config file is bundled.
