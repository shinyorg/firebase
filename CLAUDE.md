# Shiny Firebase — Working Notes

Guidance for maintaining this repo. The public documentation site is a **separate** repo at
`~/Desktop/dev/documentation` (rendered to https://shinylib.net).

## Layout

**Every shippable .NET library lives under `src/`.** Native, non-.NET code does not.

```
src/                                        # all .NET library projects (the only things that pack)
  Shiny.Push.FirebaseMessaging/             # FCM push, on top of Shiny.Push
  Shiny.DocumentDb.Firestore.Mobile/        # on-device Firestore provider for Shiny.DocumentDb
  Shiny.Firebase.iOS.Binding/               # iOS Slim Binding — Core + Messaging + Analytics (ONE framework)
  Shiny.Firebase.Firestore.iOS.Binding/
  Shiny.Firebase.Firestore.Android.Binding/ # Android (Java/AAR) binding
firebase_ios/                               # NOT .NET — native Xcode/Swift wrapper projects…
  native/{shinyfirebase,firestore}/         #   …built into .xcframeworks by
  Firebase-ios.targets                      #   Firebase-ios.targets (xcodebuild)
  merge-privacy-manifests.py                #   merges SPM privacy manifests into each framework
tests/                                      # managed-surface tests (IsPackable=false)
samples/                                    # device/emulator verification harnesses (NOT in the .slnx)
skills/                                     # published Claude Code skills, synced to shinyorg/skills
```

An iOS binding csproj reaches `firebase_ios` with `$(MSBuildThisFileDirectory)../../firebase_ios/…` — **two**
levels up, because the project sits under `src/`. Getting this wrong is the classic breakage when moving
projects around.

### One shim framework per FirebaseApp — do not split

**The Core/Messaging/Analytics shims must stay in the single `native/shinyfirebase` Xcode target.** The
Firebase SPM products are *static* libraries and `Firebase-ios.targets` passes `MERGED_BINARY_TYPE=automatic`,
so **every** framework that links them bakes in its own full copy of FirebaseCore. Two frameworks means two
`FIRApp` classes: whichever one you call `configure()` on is not the one `Messaging.messaging()` consults, and
the FCM token request silently never completes. That was shinyorg/shiny#1638 — split shims, hung
`RequestAccess()`, and an `objc[…]: Class FIRApp is implemented in both …` warning at startup as the only clue.

Verify after any change to the native projects:

```bash
nm firebase_ios/native/shinyfirebase/.build/ShinyFirebase.xcframework/ios-arm64/ShinyFirebase.framework/ShinyFirebase \
  | grep -c '_OBJC_CLASS_\$_FIRApp$'      # must be 1
```

### Privacy manifests are merged at build time

Because the Firebase SDKs are statically merged into our frameworks, **our** framework must carry their Apple
privacy manifests or App Store Connect rejects the app (ITMS-91061, shinyorg/firebase#1). SPM leaves each
dependency's `PrivacyInfo.xcprivacy` in a sibling resource bundle, so `Firebase-ios.targets` runs
`firebase_ios/merge-privacy-manifests.py` after archiving to merge them into each framework slice. Keep that
step when touching the targets, and after any native change confirm every slice has one:

```bash
find firebase_ios/native/*/.build/*.xcframework -name PrivacyInfo.xcprivacy   # 3 per framework
```

`native/firestore` is a deliberate exception: it configures and consumes FirebaseCore entirely within its own
binary, so it is self-sufficient. An app using **both** Push and Firestore still carries two copies and will
still log the duplicate-class warning — harmless today, but do not add a third.

## Package versions — Central Package Management

**Every package version lives in `Directory.Packages.props`.** Project files carry a bare
`<PackageReference Include="X" />` with **no `Version`** — adding one back fails the build (NU1008). To change
or add a package: add a `<PackageVersion Include="X" Version="…" />` centrally, then reference it bare.

## Versioning and releases — tags, not files

**The release tag is the version. There is nothing to bump in the repo.** Nerdbank.GitVersioning and the
`version.json` files are gone (removed for 7.0.0 / 5.0.0). Work happens on `main`; CI (`.github/workflows/ci.yml`)
builds every push and PR as `0.0.0-dev` without publishing. Releases come from `.github/workflows/release.yml`.

Two products ship from here, on **different version lines**, each with its own tag:

| Tag | MSBuild property | Packages |
|---|---|---|
| `v7.0.0` | `PushVersion` | `Shiny.Push.FirebaseMessaging` + `Shiny.Firebase.iOS.Binding`, `Shiny.Firebase.Firestore.iOS.Binding`, `Shiny.Firebase.Firestore.Android.Binding` |
| `firestore-v5.0.0` | `FirestoreVersion` | `Shiny.DocumentDb.Firestore.Mobile` |

`Directory.Build.props` defaults both properties to `0.0.0-dev` and sets `Version` from `PushVersion`; the Firestore
csproj overrides `Version` with `FirestoreVersion`. **Never pass `-p:Version`** — it is a global property and
stamps every project, both lines, with the same number.

The Firestore bindings ride the **Push** line, so a `firestore-v*` release depends on them at the nearest `v*`
tag behind the released commit (`git describe --match 'v[0-9]*'`). If the bindings changed, tag the Push line
first. A suffix (`v7.0.0-beta.1`) makes a prerelease. `release.yml` can also be run by hand with a product and
version; it creates the tag.

**History before tags.** No tags exist before `v7.0.0` / `firestore-v5.0.0`. Nerdbank used a 3-part
`version.json`, so the NuGet version stayed fixed until someone edited the file, and every push to a `v*` branch
re-built the same version and published nothing (`--skip-duplicate`). Commits after a version's first publish
silently did not ship. NuGet publish timestamps are the source of truth for what shipped in a pre-7.0 release. Do
not retro-tag old commits.

Published lines going in: Push 6.0.0 (and the three bindings) → 7.0.0, Firestore 4.0.1 → 5.0.0.

- **`Shiny.Push.FirebaseMessaging`** (7.x) wraps Firebase Cloud Messaging on top of the Shiny Push
  infrastructure — the native Firebase iOS SDK via the bindings above on iOS, and `Shiny.Push`'s built-in FCM
  support on Android. The public surface is the `AddPushFirebaseMessaging` extensions and the
  `FirebaseConfiguration` record; everything else builds on the `Shiny.Push` contracts (`IPushManager`,
  `IPushDelegate`, `IPushProvider`, `IPushTagSupport`).
- **`Shiny.DocumentDb.Firestore.Mobile`** (5.x) is an on-device Firestore provider for Shiny.DocumentDb. Its
  docs, release notes, skill and version line all differ from the Push package's — see
  [Firestore Mobile provider](#firestore-mobile-provider) below before touching it.

## After every new feature or fix

A change is not "done" until the four artifacts below are in sync. Do all of them in the same
change unless there's a reason not to.

1. **Code + build** (`src/Shiny.Push.FirebaseMessaging/`, `src/Shiny.Firebase.*.iOS.Binding/`)
   - Shared registration lives in `Platforms/Shared/ServiceCollectionExtensions.cs`; iOS-specific
     provider logic in `Platforms/iOS/`. Keep the `#if IOS` / `#if ANDROID` paths in sync — the
     Android path delegates to `Shiny.Push`'s `FirebaseConfig`, the iOS path registers
     `FirebasePushProvider`.
   - Verify with `dotnet build Firebase.slnx`. iOS binding builds invoke `xcodebuild` to produce the
     xcframeworks, so they require a Mac with the matching Xcode/iOS SDK.
   - There is no test project for the Push code — `tests/` only covers the Firestore provider's managed
     surface. Samples are **not** in the `.slnx` (they are app heads), so a solution build does not catch a
     broken reference in one; build them explicitly after moving or renaming projects.
   - When bumping the `Shiny.Push` package reference, confirm the consumed contracts still match
     (the `IPushDelegate` / `IPushProvider` / `FirebaseConfig` signatures) and that the platform
     SDK versions line up with what `Shiny.Push` targets.

2. **Documentation site** (`~/Desktop/dev/documentation/src/content/docs/client/push/`)
   - Update the relevant page — the Firebase iOS guide is `firebase-ios.mdx`; cross-cutting push
     behavior may also touch `native.mdx`, `architecture.mdx`, or `faq.mdx`.
   - Add a **release note** — see the release-note rules below.
   - Pages are `.mdx`; release notes use the `<RN>` component
     (`import RN from '/src/components/ReleaseNote.astro'`), with `type="feature|enhancement|fix|breaking"`
     and an optional `platform="iOS|Android"`.

3. **Skill** (`skills/shiny-firebase/SKILL.md`)
   - This is the source of the published `shiny-firebase` Claude Code skill — the agent-facing
     "how to generate correct code" doc. It is synced to the `shinyorg/skills` repo (under
     `plugins/shiny/skills/shiny-firebase` — every Shiny skill lives in the single `shiny`
     plugin) by `.github/workflows/sync-skills.yml`, which `release.yml` calls after a release ships (or run it
     by hand) — so the published skill matches a shipped version.
   - Keep `SKILL.md` aligned with the code. Update the `triggers:` keyword list in the frontmatter
     when a new public type / API is introduced.
   - If the default or recommended pattern changes, the skill's default guidance must change too.

4. **readme.md** (repo root)
   - This file is packed into the NuGet package (`PackageReadmeFile` in `Directory.build.props`).
     Update the feature list and any inline guidance when behavior changes.

## Release notes

Firebase ships as part of the Push module, so release notes live in the documentation repo alongside the rest
of the Shiny client packages, at `~/Desktop/dev/documentation/src/content/docs/client/release-notes.mdx`. Each
release there is broken down by component — put Firebase notes under a `#### Push Notifications` subheading.

**Which version does a note go against?** The next tag on that product's line — one past the latest
`v*` (Push) or `firestore-v*` (Firestore) tag, or past the latest NuGet version if the line has no tag yet. Use the
raw version only (strip any prerelease suffix, e.g. `7.1.0-beta.1` → `7.1.0`).

**Heading style — match the existing file.** Releases are grouped under a `## v<major>` heading,
with each release as `### Firebase Push <major>.<minor>.<patch> - <date>` (e.g. `### Firebase Push 7.0.0 - TBD`).
The **`Firebase Push` prefix is required**: the file is shared with the Shiny client packages, which are on the
same major line, so a bare `### 7.0.0` collides with (and reads as) the client release of the same number. Older Firebase
notes predate the prefix and sit under client headings — leave them where they are.

**If the version isn't released yet (beta / prerelease, or work-in-progress for the next version):**
- If a `### <version> - TBD` heading already exists, **add the note under that existing section**.
  If you're modifying a feature that hasn't shipped yet (already an entry under a `TBD` section),
  edit that existing entry in place rather than adding a duplicate.
- If no section exists for that version yet, **create a new `### <version> - TBD` heading** (under
  the matching `## v<major>` group, creating that group if needed) at the top and add the note there.

**If the version is a final release**, the section is dated (`### 4.0.0 - March 26, 2026`); add the
note under the matching dated section (or promote the `TBD` section to a dated one when cutting the
release).

Each note is a single `<RN>` line. Use `type="breaking"` for breaking changes (it's its own note
type here, not a flag) and `platform="iOS"` / `platform="Android"` when a change is platform-specific.
Newest version group stays at the top of the file.

**MDX build gotchas** (these only surface at build time, so always run `npm run build` in the
documentation repo after editing content):
- An `<RN>` entry that contains a code fence needs its opening/closing tags on their own lines, or
  the MDX build fails.
- When documenting an API, verify it against the real code in this repo rather than inventing
  surface — the docs repo has no access to the library source.

## Firestore Mobile provider

`Shiny.DocumentDb.Firestore.Mobile` follows the same four-artifact rule, but every artifact lives somewhere else:

- **Version:** its own `firestore-v*` tag line (`FirestoreVersion`), not the Push `v*` tags — see
  [Versioning and releases](#versioning-and-releases--tags-not-files).
- **Docs + release notes:** `~/Desktop/dev/documentation/src/content/docs/documentdb/firestore-mobile.mdx`. Its
  releases are listed in that page's own `## Release notes` section as `### <version> - <date|TBD>` (no
  `## v<major>` grouping) — **not** in `documentdb/release-notes.mdx`, which versions DocumentDB itself.
- **Skill:** `skills/shiny-firestore-mobile/SKILL.md`.
- **readme.md:** the `# Mobile Firestore` half of the root readme.
- **Tests:** `tests/` covers its managed surface; the native heads are verified against the Firestore emulator
  with the harnesses under `samples/`.

## Blog posts (only when explicitly requested)

Do **not** write blog posts automatically as part of a fix/feature. Write them **only when the user asks**. When asked to blog a feature, produce **two** posts — first the docs-site version, then adapt it for the personal blog.

### 1. Docs site — `~/Desktop/dev/documentation`

- File: `src/content/docs/blog/YYYY/MM/<slug>.mdx` (current year/month folders; create the month folder if needed).
- Frontmatter:
  ```yaml
  ---
  title: '...'
  description: '...'
  date: YYYY-MM-DD
  authors:
    - allanritchie
  tags:
    - Release        # or Feature, AI, etc.
  ---
  ```
- Body is MDX. Reuse components where relevant, e.g. `import NugetBadge from '/src/components/NugetBadge.astro';` then `<NugetBadge name="Shiny.Push.FirebaseMessaging" />`.
- Voice: product/release-note tone — what shipped, breaking changes, code samples, how to use it. **No hero image** on this site.

### 2. Personal blog — `~/Desktop/dev/blog` (adapt the docs post)

- File: `src/content/blog/YYYY/MM/<slug>.mdx` (note: `content/blog`, not `content/docs/blog`).
- Frontmatter (different schema — see `src/content.config.ts`):
  ```yaml
  ---
  title: '...'
  description: '...'
  pubDate: 'Mon DD YYYY'                          # e.g. 'Jun 15 2026'
  heroImage: '../../../../assets/<slug>-hero.svg'
  tags: ['Shiny', '.NET']
  ---
  ```
- Voice: rework the docs post into a personal, first-person narrative ("Here's something that shouldn't be hard but is…", "So I built…") — story/motivation up front, not a dry changelog.
- **Hero image is required.** Create `src/assets/<slug>-hero.svg`:
  - SVG, `viewBox="0 0 1200 630"`, `width="1200" height="630"`.
  - Match the house style: dark navy/indigo gradient background (`#0f172a` → `#1e1b4b`), cyan/green/violet accent gradients, subtle glow filters, the feature name as the headline. Crib an existing one as a starting template.
