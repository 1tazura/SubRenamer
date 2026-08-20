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

The generic performance pass is now accepted on the representative 25-archive / 50-subtitle workload. Revisit this area only when a new device/provider/archive format exposes a specific measured regression rather than continuing unconditional concurrency tuning.

### Preview baseline

On the measured 25-video / 50-subtitle workload, preview work is already small:

- Core Diff: roughly 20–80 ms;
- target-folder snapshot: roughly 0.04–0.3 s depending on run;
- total preview backend: roughly 0.12–0.8 s.

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

#### v0.1.20 scan cleanup and acceptance

The main scan enumerates direct children of `Download` exactly once, capturing both the `Torrent` folder and direct subtitle/archive candidates. Torrent **subtree** traversal and archive indexing then run in parallel. This removes the two competing root enumerations observed in v0.1.19.

The known deterministic unsupported-stream error is stored as a conservative stable rejection under the same identity/size/mtime signature. Unchanged non-archive bytes therefore are not reopened every warm scan. Transient errors remain uncached and are retried.

Representative warm scans after the stable rejection is cached have measured roughly 1.1–1.6 seconds total, with Torrent subtree traversal around 0.1–0.2 seconds and archive indexing around 0.2–0.4 seconds. Provider/root enumeration variability is now the largest scan component and is not worth weakening the fixed SAF workflow to chase further.

See `ARCHIVE_INDEX_CACHE.md` for the exact policy.

### Apply / processing performance

v0.1.20 instrumentation on a 50-subtitle archive workload writing 22,064,348 bytes measured:

- destination creation: 6333 ms;
- destination open: 1708 ms;
- transfer / decompression / SHA-256: 2546 ms;
- `ApplyService` total: 10,872 ms;
- click-to-complete: 10,912 ms.

This made SAF destination preparation the dominant actionable bottleneck. v0.1.21 therefore introduced a bounded look-ahead pipeline that prepares up to four destination files concurrently while keeping archive extraction, hashing and actual subtitle writes strictly sequential.

On the same representative workload v0.1.21 measured:

- destination creation cumulative: 3914 ms;
- destination open cumulative: 858 ms;
- actual destination-ready wait visible to the sequential consumer: 538 ms;
- transfer / decompression / SHA-256: 907 ms;
- `ApplyService` total: 1728 ms;
- click-to-complete: 1762 ms;
- 50 successful outputs / 0 skipped / 0 failed.

The cumulative provider timings remain large because they overlap. The user-visible Apply wall clock fell from about 10.9 seconds to about 1.7 seconds. Concurrency 4 is therefore the accepted generic default: increasing it further has a small theoretical upside compared with additional provider pressure and a larger pre-created cleanup window.

Transfer/decompression/SHA is now the largest remaining measured phase, but it represents real data work. SHA-256 is intentionally retained for safe Undo and is already computed in the same one-pass 256 KiB copy loop. Do not parallelize multiple entries from one SharpCompress session merely to reduce this counter; solid 7z sources in particular require format-specific measurement first.

See `PROCESSING_PERFORMANCE.md`.

### Remaining performance work — only when triggered by evidence

- investigate solid-7z batch/streaming extraction if a real solid-7z source is measurably slow;
- investigate Android-native document creation only if another provider/device shows substantial `等待目标就绪` again;
- retain scan/apply diagnostics so regressions remain visible;
- do not increase archive or output concurrency merely to improve synthetic counters.

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
