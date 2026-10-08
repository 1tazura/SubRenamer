# Fork overview

This repository is a fork of [qwqcode/SubRenamer](https://github.com/qwqcode/SubRenamer).

The upstream desktop application remains the reference implementation for the original SubRenamer feature set. This fork currently adds an **Android adaptation** under [`Android/`](Android/) while deliberately keeping the original [`SubRenamer.Core`](SubRenamer.Core/) episode-matching implementation as the matching engine.

## What is different in this fork?

The Android application is not a pixel-for-pixel port of the desktop UI. It is designed around a specific mobile/torrent workflow:

- videos are already downloaded under `Download/Torrent/**` and may still be seeding;
- subtitle archives or loose subtitle files are placed in the root of `Download`;
- the app finds the likely torrent directory, asks for confirmation when attribution is ambiguous, delegates episode mapping to `SubRenamer.Core`, and writes matched subtitles beside the videos;
- torrent video files are treated as read-only inputs and are never moved, renamed, copied, or deleted.

The Android port also has one feature that upstream currently does not expose as a one-click operation: **safe undo of the most recent Android placement batch**. Undo only deletes files that the Android app created and whose SHA-256 content still matches the recorded output.

## Documentation map

Android-specific documentation lives under [`Android/`](Android/):

- [`Android/README.md`](Android/README.md) — user-facing entry point, supported workflow, build/install notes.
- [`Android/docs/FEATURES.md`](Android/docs/FEATURES.md) — upstream desktop vs Android feature matrix and porting decisions.
- [`Android/docs/ROADMAP.md`](Android/docs/ROADMAP.md) — prioritized work that remains.
- [`Android/docs/ARCHITECTURE.md`](Android/docs/ARCHITECTURE.md) — storage, matching and safety boundaries.
- [`Android/docs/WORKFLOW.md`](Android/docs/WORKFLOW.md) — concrete end-to-end example.
- [`Android/docs/VALIDATION.md`](Android/docs/VALIDATION.md) — CI and APK validation contract.

The existing root [`README.md`](README.md) is intentionally kept close to upstream so that upstream documentation and screenshots do not have to be duplicated or continuously rebased. Fork-specific divergence should be documented here and under `Android/` instead.

## Current development branch

Android development is currently carried by the `android-port-v1` branch / pull request. Until it is merged into the fork's default branch, GitHub's default root README may still look identical to upstream.

## License

The fork remains under the repository's GPL-2.0 license. See [`LICENSE`](LICENSE).
