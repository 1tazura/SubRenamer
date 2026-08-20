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

The first low-risk performance pass reduced repeated SAF enumeration, added bounded folder/archive concurrency and made preview/scan timing visible on-device. Later passes added persistent archive indexing, real top-level overlap and apply-phase diagnostics.

### Preview baseline

On the measured 25-video / 50-subtitle workload, preview work is already small:

- Core Diff: roughly 20–80 ms;
- target-folder snapshot: roughly 0.07–0.3 s depending on run;
- total preview backend: roughly 0.15–0.8 s.

Preview is not currently a priority bottleneck.

### Scan history

The original scan profile was roughly:

- Torrent traversal: 1.9–2.0 s;
- Download-root enumeration: 9.0–9.6 s;
- indexing 25 archives: 3.7–4.2 s;
- work attribution: about 0.7 s;
- total: about 16.1 s.

#### v0.1.12 SAF-name optimization

Avalonia Android `GetItemsAsync()` already returns a document id and MIME type in one cursor, but `IStorageItem.Name` performs another `ContentResolver` metadata query. Calling `.Name` for every item in a large `Download` directory turned one enumeration into hundreds of extra IPC/provider queries.

For Android's `com.android.externalstorage.documents` provider, the document id embedded in `IStorageItem.Path` already contains the item path/name. v0.1.12 derives the display name from that URI when possible and falls back to `IStorageItem.Name` for other providers/platforms.

Real-device measurement reduced Download-root enumeration from roughly 9 seconds to roughly 1.1 seconds and the full eager scan from roughly 16 seconds to roughly 6.5 seconds.

#### Archive-validation decision

A later experiment deferred archive inspection until the user selected an archive. It reduced the initial scan to roughly 2.5 seconds, but it also meant arbitrary non-subtitle ZIP/7z/RAR files in `Download` had to appear as unverified candidates and weakened the automatic `source -> torrent target` workflow until the user made a manual source choice.

That trade-off was rejected. The Android workflow intentionally keeps **eager validation of archive contents during scanning**: only archives that actually contain supported subtitle entries become subtitle sources, and their internal filenames remain immediately available to automatic work attribution.

Do not reintroduce lazy archive discovery merely to improve the headline scan time unless the automatic-selection semantics can be preserved.

#### v0.1.17–v0.1.18 archive-index cache

A completed archive inspection is cached persistently using storage identity + byte size + last-modified timestamp. Positive results store the complete real subtitle-entry list; negative results store the fact that a fully opened archive contained no supported subtitles. Missing metadata conservatively disables reuse.

On the measured device, v0.1.18 changed archive indexing from about 5303 ms on a cold scan to about 422 ms on an immediate warm scan. Total warm scan time was about 2797 ms.

#### v0.1.19 real top-level overlap

The video-target and subtitle-source scan chains were moved onto independent workers. Real-device warm-scan measurement then showed:

- Torrent branch: about 1465 ms;
- Download root: about 1489 ms;
- archive indexing: about 548 ms;
- work attribution: about 77 ms;
- total: about 2134 ms.

The total closely matched `root/subtitle critical path + attribution` rather than the sum of both branches, confirming real overlap.

The same run identified the previously unexplained 25th archive as an unrelated file named `支付宝交易明细(20251213-20260313).zip`, rejected by SharpCompress with `ArchiveOperationException: Cannot determine compressed stream type.`

#### v0.1.20 scan cleanup

The main scan now enumerates direct children of `Download` exactly once, capturing both the `Torrent` folder and direct subtitle/archive candidates. Torrent **subtree** traversal and archive indexing then run in parallel. This removes the two competing root enumerations observed in v0.1.19.

The known deterministic unsupported-stream error can now be stored as a conservative stable rejection under the same identity/size/mtime signature. Unchanged non-archive bytes therefore are not reopened every warm scan. Transient errors remain uncached and are retried.

See `ARCHIVE_INDEX_CACHE.md` for the exact policy.

### Apply / processing performance

As scanning approached roughly two seconds, the actual subtitle placement step became comparatively prominent. v0.1.20 therefore instruments the apply path rather than guessing at the next bottleneck.

The UI reports cumulative time for:

- target-directory no-overwrite recheck;
- archive/source preparation;
- destination creation;
- destination open;
- loose-source open;
- transfer / archive decompression / SHA-256 / write;
- destination close/commit;
- undo-journal persistence;
- full click-to-result wall time.

Two low-risk changes are already included:

- copy/hash and non-seekable archive staging use 256 KiB buffers to reduce provider/native calls;
- redundant explicit `FlushAsync` immediately before output-stream disposal was removed; close/dispose remains the required commit boundary.

No blind write concurrency or parallel extraction has been introduced. If real-device timings show `CreateFileAsync`/open/close dominates, bounded destination-side pipelining can be evaluated. If transfer/decompression dominates—especially on solid 7z sources—optimize the archive extraction strategy instead of increasing SAF concurrency.

See `PROCESSING_PERFORMANCE.md`.

### Remaining performance work

- measure v0.1.20 warm scan after shared-root enumeration and cached stable rejection;
- measure one representative real apply and use its phase breakdown to select the next optimization;
- do not increase `ArchiveScanConcurrency` or output concurrency blindly;
- investigate solid 7z batch/streaming extraction only if transfer/decompression is shown to dominate.

The fixed `Download` / `Torrent` storage boundary remains useful. Do not shift routine folder-selection work back to the user solely for scan speed.

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
