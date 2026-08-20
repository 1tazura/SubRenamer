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

## P1 — make automatic matching recoverable

The current basic flow works, but users need an escape hatch when automatic matching is wrong or incomplete.

### Per-item correction / exclusion

Add a touch-friendly editor for the generated plan:

- change the video target for one subtitle;
- exclude one item from processing;
- clearly identify unmatched/conflicting rows;
- regenerate destination names safely after edits.

This should come before advanced rule editors because it solves many real-world SP/OVA/NCOP cases with lower complexity.

### Manual matching mode

Expose the upstream Core manual matching mode in Android rather than reimplementing it.

Requirements:

- separate work-level attribution from episode-level matching UI;
- support upstream manual video/subtitle rules;
- show immediate preview before apply;
- persist useful rules where appropriate.

### Regex matching mode

Expose `MatcherOptions.VideoRegex` / `SubtitleRegex` through a mobile editor with test feedback.

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

Next steps should be measurement-driven:

- expose elapsed time for video discovery, subtitle/archive discovery, attribution, preview and apply;
- identify whether SAF `CreateFileAsync` is the dominant apply bottleneck;
- if justified, add an Android-specific `DocumentsContract.CreateDocument` path to avoid redundant directory scans while preserving conflict guarantees;
- optimize solid 7z extraction as a batch/streaming operation if repeated random extraction proves expensive.

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
