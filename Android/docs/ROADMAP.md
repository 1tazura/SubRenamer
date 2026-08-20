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

The first low-risk performance pass already reduces repeated SAF enumeration, adds bounded folder/archive concurrency and indexes archive entries.

Preview diagnostics separately report:

- original `SubRenamer.Core` matching time;
- target-folder SAF enumeration time;
- Android plan-generation time;
- total preview-backend time.

Real-device measurements showed the Core itself is small on the tested 25-video / 50-subtitle workload (tens of milliseconds), so Core optimization is no longer the current priority.

The next scan-profiling pass now also:

- starts Torrent video discovery and Download subtitle discovery concurrently instead of serially;
- reports Torrent traversal time;
- reports Download-root enumeration time;
- reports archive-indexing time and archive count;
- reports loose-source finalization time;
- reports work-level attribution time;
- reports total scan wall time.

Use these measurements to decide whether the next structural optimization should be **lazy archive indexing** or **incremental/cached Torrent discovery**.

Likely next steps after real-device scan timings:

- if archive indexing dominates, list archive files initially without opening all of them and index only the selected/needed archive; use archive filename evidence first and expand internal filenames only when attribution requires it;
- if Torrent traversal dominates, persist an index of known video-target directories and provide a fast refresh plus an explicit full rescan;
- identify whether SAF `CreateFileAsync` is the dominant apply bottleneck;
- if justified, add an Android-specific `DocumentsContract.CreateDocument` path to avoid redundant directory scans while preserving conflict guarantees;
- optimize solid 7z extraction as a batch/streaming operation if repeated random extraction proves expensive.

The fixed `Download` / `Torrent` storage boundary should be retained unless measurements show a reason to change it. It provides a stable SAF authorization and safety boundary; performance should first be improved by replacing eager full rescans with lazy/incremental work rather than shifting folder-selection work back to the user.

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
