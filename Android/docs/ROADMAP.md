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

The first low-risk performance pass reduces repeated SAF enumeration, adds bounded folder/archive concurrency and indexes archive entries. Preview and scan timing are now visible on-device.

### Measured bottlenecks

On the 25-video / 50-subtitle test workload, preview work is already small:

- Core Diff: roughly 20–80 ms;
- target-folder snapshot: roughly 0.3 s;
- total preview backend: roughly 0.6–0.8 s.

The original scan profile identified the real wall-clock problem:

- Torrent traversal: roughly 1.9–2.0 s;
- Download-root enumeration: roughly 9.0–9.6 s;
- indexing 25 archives: roughly 3.7–4.2 s;
- work attribution: roughly 0.7 s;
- total scan: roughly 16.1 s.

### v0.1.12 SAF-name optimization

Avalonia Android `GetItemsAsync()` already returns a document id and MIME type in one cursor, but `IStorageItem.Name` performs another `ContentResolver` metadata query. Calling `.Name` for every item in a large `Download` directory turns one enumeration into hundreds of extra IPC/provider queries.

For Android's `com.android.externalstorage.documents` provider, the document id embedded in `IStorageItem.Path` already contains the item path/name. v0.1.12 derives the display name from that URI when possible and falls back to `IStorageItem.Name` for other providers/platforms.

Real-device measurement reduced Download-root enumeration from roughly 9 seconds to roughly 1.1 seconds, and the full eager scan from roughly 16 seconds to roughly 6.5 seconds.

This optimization is applied to:

- Download-root source discovery;
- Torrent folder/video discovery;
- child-folder/file lookup and snapshots;
- archive filename handling;
- Core video-name input;
- work attribution;
- loose-subtitle lookup.

### Archive-validation decision

A later experiment deferred archive inspection until the user selected an archive. It reduced the initial scan to roughly 2.5 seconds, but it also meant arbitrary non-subtitle ZIP/7z/RAR files in `Download` had to appear as unverified candidates and weakened the automatic `source -> torrent target` workflow until the user made a manual source choice.

That trade-off was rejected. The Android workflow intentionally restores **eager validation of archive contents during scanning**: only archives that actually contain supported subtitle entries become subtitle sources, and their internal filenames remain immediately available to automatic work attribution.

The extra few seconds are currently considered preferable to sacrificing automatic source discovery. Do not reintroduce lazy archive discovery merely to improve the headline scan time unless the automatic-selection semantics can be preserved.

### v0.1.17 validated archive-index cache

Eager validation is retained, but a completed archive inspection is now cached persistently using the archive storage identity plus byte size and last-modified timestamp. The cached result includes both positive results (the real subtitle-entry list) and negative results (the archive was fully inspected and contained no supported subtitles).

On later scans, an archive is reused only when all signature fields still match. New or changed archives are fully opened and validated exactly as before. If the current storage provider cannot supply both size and modification time, caching is disabled for that file and it is fully validated every time. Cache read/write failures are also treated as optimization failures only and never prevent a normal scan.

This preserves the same validated-source discovery and automatic attribution evidence while avoiding repeated archive parsing for unchanged files. The first scan after installing the version still pays the normal eager-validation cost; subsequent scans should primarily pay metadata checks plus full validation only for new/changed archives.

### Remaining performance work

- Torrent traversal is now roughly 1.1 s on the measured device and is not urgent.
- Measure first-scan versus warm-cache scan time with the 25-archive workload before attempting more archive concurrency or format-specific parsers.
- identify whether SAF `CreateFileAsync` is the dominant apply bottleneck only after higher-value functionality work;
- optimize solid 7z extraction as a batch/streaming operation if repeated random extraction proves expensive.

The fixed `Download` / `Torrent` storage boundary remains useful. Do not shift routine folder-selection work back to the user solely for scan speed.

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
