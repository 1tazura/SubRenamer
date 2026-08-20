# Android roadmap

This roadmap is organized by user value and risk rather than by literal desktop feature parity.

## P0 — protect the working path

These are invariants, not optional features:

- never modify torrent video bytes, names, paths, or directory structure;
- never overwrite an existing destination subtitle;
- preserve source archives / loose subtitles;
- keep episode matching delegated to the original `SubRenamer.Core` unless an upstream-compatible Core change is deliberately reviewed;
- keep standalone APK validation in CI;
- retain safe undo semantics: only delete outputs created by the app and unchanged since creation.

## P1 — make matching recoverable

### Core matching modes — implemented

Android now exposes all Core matching paths needed for parity with the desktop matching engine:

- automatic Diff mode;
- desktop-compatible manual patterns where `$$` marks the key and `*` is a wildcard;
- direct Regex mode using capture group 1 as the key;
- persisted mode/rules;
- preview before apply.

The first Android UI is intentionally compact rather than a literal port of the desktop rule-editor windows. Rich sample-file testing can be added later if needed.

### Per-item correction / exclusion — next

Add a touch-friendly editor for the generated plan:

- change the video target for one subtitle;
- exclude one item from processing;
- clearly identify unmatched/conflicting rows;
- regenerate destination names safely after edits.

This is now the main missing escape hatch for SP/OVA/NCOP and unusual packs after Manual/Regex support landed.

## P2 — language and output control

### Language filtering

For sources containing CHS/CHT/ENG/JPN/etc., allow the user to choose:

- all detected tracks;
- one or more language tags;
- remember a default preference if desired.

Filtering must happen before apply and remain visible in preview.

### Output naming settings

Add settings for:

- preserve detected language suffix;
- custom appended suffix;
- possibly custom extension recognition.

Do not weaken no-overwrite behavior.

### Settings surface

Create a real Android settings page instead of continuing to hard-code policy into the main screen. Keep safety-critical invariants non-disableable unless there is a very strong reason.

## P3 — performance and observability

Preview and scan timing are visible on-device so optimization can follow measured costs rather than assumptions.

### Preview measurements

On the 25-video / 50-subtitle test workload, preview work is already small:

- Core Diff: roughly 20–80 ms;
- target-folder snapshot after the SAF-name optimization: roughly 0.08 s;
- total preview backend: roughly 0.19 s in the latest measured run.

Core optimization is therefore not a priority.

### Scan measurements

Before the SAF-name optimization, the same device measured roughly:

- Torrent traversal: 1.9–2.0 s;
- Download-root enumeration: 9.0–9.6 s;
- indexing 25 archives: 3.7–4.2 s;
- work attribution: about 0.7 s;
- total scan: about 16.1 s.

v0.1.12 removed repeated Android metadata queries for item names. The measured result was:

- Torrent traversal: **1.16 s**;
- Download-root enumeration: **1.13 s**;
- indexing 25 archives: **4.14 s**;
- work attribution: **0.078 s**;
- total scan: **6.51 s**.

That confirms the fixed `Download` / `Torrent` boundary is not the fundamental problem. Root enumeration fell by almost an order of magnitude, and eager archive indexing became the dominant remaining scan cost.

### Lazy archive indexing — implemented for v0.1.14

Initial scanning now discovers archive filenames without opening all archives. Archive contents are indexed only when that source is selected, after which source-to-target attribution is recomputed using package-internal subtitle names.

This changes the cost model from roughly:

```text
scan = open every archive in Download
```

to:

```text
scan = enumerate archive candidates
select source = open/index one archive
```

The on-device scan diagnostics remain visible so this change can be measured directly.

### Possible next performance steps

After measuring lazy indexing:

- if Torrent traversal around one second is still material, consider a persistent target index / fast refresh plus explicit full rescan;
- if selected-archive indexing is unexpectedly slow for particular solid 7z files, optimize extraction/indexing around sequential archive access;
- only then revisit apply-path `CreateFileAsync` overhead if writing remains worth optimizing.

The fixed `Download` / `Torrent` storage boundary remains useful and should not be replaced merely to avoid work the app can skip itself.

Avoid increasing concurrency blindly; Android `DocumentsProvider` / storage backends can regress under excessive parallelism.

## P4 — operation history

Current Android undo stores one batch.

A future history model could keep several recent batches with:

- target title/path;
- timestamp;
- created files;
- content hashes;
- undo eligibility/status.

This is more useful for the Android copy-based model than upstream's in-place backup mechanism.

## P5 — subtitle synchronization

Porting FFsubsync / FFmpeg is valuable but comparatively large.

Before implementation, define:

- Android binary/runtime distribution;
- architecture support;
- storage access and temporary-file strategy;
- background execution/progress behavior;
- interaction with undo/history.

Do not bundle this into the basic rename/placement path until the mobile execution model is reliable.

## Lower-priority parity

Consider later:

- multi-language UI;
- explicit theme setting;
- open/reveal in external file manager;
- update checking after a stable Android release channel exists.

## Intentionally not direct-porting

Do not spend parity effort on desktop-only interaction patterns unless a mobile use case emerges:

- drag-and-drop as the primary import mechanism;
- always-on-top windows;
- desktop keyboard shortcut sets;
- Linux rename-command clipboard actions.

## Documentation rule

When Android behavior changes, update the relevant document in the same change:

- user-visible capability → `FEATURES.md` and, if needed, `Android/README.md`;
- priority/scope → `ROADMAP.md`;
- storage/safety/flow invariant → `ARCHITECTURE.md`;
- build/test guarantee → `VALIDATION.md`.
