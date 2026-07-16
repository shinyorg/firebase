# Shiny Firebase — Working Notes

Guidance for maintaining this repo. The public documentation site is a **separate** repo at
`~/Desktop/dev/documentation` (rendered to https://shinylib.net).

## Layout

**Every shippable .NET library lives under `src/`.** Native, non-.NET code does not.

```
src/                                        # all .NET library projects (the only things that pack)
  Shiny.Push.FirebaseMessaging/             # FCM push, on top of Shiny.Push
  Shiny.DocumentDb.Firestore.Mobile/        # on-device Firestore provider for Shiny.DocumentDb
  Shiny.Firebase.Analytics.iOS.Binding/     # iOS Slim Bindings
  Shiny.Firebase.Messaging.iOS.Binding/
  Shiny.Firebase.Firestore.iOS.Binding/
  Shiny.Firebase.Firestore.Android.Binding/ # Android (Java/AAR) binding
firebase_ios/                               # NOT .NET — native Xcode/Swift wrapper projects…
  native/{analytics,messaging,firestore}/   #   …built into .xcframeworks by
  Firebase-ios.targets                      #   Firebase-ios.targets (xcodebuild)
tests/                                      # managed-surface tests (IsPackable=false)
samples/                                    # device/emulator verification harnesses (NOT in the .slnx)
skills/                                     # published Claude Code skills, synced to shinyorg/skills
plans/
```

An iOS binding csproj reaches `firebase_ios` with `$(MSBuildThisFileDirectory)../../firebase_ios/…` — **two**
levels up, because the project sits under `src/`. Getting this wrong is the classic breakage when moving
projects around.

## Package versions — Central Package Management

**Every package version lives in `Directory.Packages.props`.** Project files carry a bare
`<PackageReference Include="X" />` with **no `Version`** — adding one back fails the build (NU1008). To change
or add a package: add a `<PackageVersion Include="X" Version="…" />` centrally, then reference it bare.

`Nerdbank.GitVersioning` is applied to every project from `Directory.Build.targets`:

```xml
<PackageReference Include="Nerdbank.GitVersioning" PrivateAssets="All"/>
```

with its version supplied centrally like everything else. **`PrivateAssets="All"` is load-bearing** — it stops
Nerdbank flowing into each library's `.nuspec` as a dependency. Don't drop it, and don't add a per-project
`PackageReference Update` to override the version; that is what this looked like before CPM, and the overrides
had already drifted (most projects on 3.10.91, the Android binding and both samples silently on 3.9.50).

If versioning ever looks wrong — e.g. a package suddenly reporting `1.0.0` — suspect that Nerdbank stopped
being injected. `Shiny.Push.FirebaseMessaging` reading anything other than `5.0.1` is the giveaway, since
`1.0.0` is MSBuild's default `PackageVersion` and looks plausible.

Two products ship from here, on **different version lines** — see `version.json` at the root vs the nested one
under the Firestore provider:

- **`Shiny.Push.FirebaseMessaging`** (5.x) wraps Firebase Cloud Messaging on top of the Shiny Push
  infrastructure — the native Firebase iOS SDK via the bindings above on iOS, and `Shiny.Push`'s built-in FCM
  support on Android. The public surface is the `AddPushFirebaseMessaging` extensions and the
  `FirebaseConfiguration` record; everything else builds on the `Shiny.Push` contracts (`IPushManager`,
  `IPushDelegate`, `IPushProvider`, `IPushTagSupport`).
- **`Shiny.DocumentDb.Firestore.Mobile`** (1.x) is an on-device Firestore provider for Shiny.DocumentDb. It has
  its **own `CLAUDE.md`** — read that before touching it; its docs, release notes, and versioning rules all
  differ from this file's.

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

2. **Documentation site** (`~/Desktop/dev/documentation/src/content/docs/push/`)
   - Update the relevant page — the Firebase iOS guide is `firebase-ios.mdx`; cross-cutting push
     behavior may also touch `native.mdx`, `architecture.mdx`, or `faq.mdx`.
   - Add a **release note** — see the release-note rules below.
   - Pages are `.mdx`; release notes use the `<RN>` component
     (`import RN from '/src/components/ReleaseNote.astro'`), with `type="feature|enhancement|fix|breaking"`
     and an optional `platform="iOS|Android"`.

3. **Skill** (`skills/shiny-firebase/SKILL.md`)
   - This is the source of the published `shiny-firebase` Claude Code skill — the agent-facing
     "how to generate correct code" doc. It is synced to the `shinyorg/skills` repo (under
     `plugins/shiny-client/skills/shiny-firebase`) by `.github/workflows/sync-skills.yml`.
   - Keep `SKILL.md` aligned with the code. Update the `triggers:` keyword list in the frontmatter
     when a new public type / API is introduced.
   - If the default or recommended pattern changes, the skill's default guidance must change too.

4. **readme.md** (repo root)
   - This file is packed into the NuGet package (`PackageReadmeFile` in `Directory.build.props`).
     Update the feature list and any inline guidance when behavior changes.

## Release notes

Firebase ships as part of the Push module, so release notes live in the documentation repo at
`~/Desktop/dev/documentation/src/content/docs/push/release-notes.mdx`.

**Which version does a note go against?** Use the `version` field in `version.json` (this repo uses
Nerdbank.GitVersioning) — **the raw version portion only** (strip any prerelease/build-metadata
suffix, e.g. `5.0.0-beta` → `5.0.0`).

**Heading style — match the existing file.** Releases are grouped under a `## v<major>` heading,
with each release as `### <major>.<minor>.<patch> - <date>` (e.g. `### 4.0.0 - March 26, 2026`).

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
