# SubRenamer Android

Android adaptation of [SubRenamer](https://github.com/qwqcode/SubRenamer), built around the original [`SubRenamer.Core`](../SubRenamer.Core/) matching algorithm.

> **Current status:** the basic Android workflow has been validated on a real device: authorize `Download` → scan → attribute subtitle source to torrent target → preview Core mapping → place subtitles → undo the last placement batch.

This port is intentionally **torrent-safe**. It assumes that video files under `Download/Torrent/**` may still be seeding and therefore treats them as read-only inputs.

## Supported storage workflow

```text
/storage/emulated/0/Download/
├─ Torrent/
│  ├─ Some.Release/
│  │  ├─ episode01.mkv
│  │  ├─ episode02.mkv
│  │  └─ ...
│  └─ Another.Release/
│     └─ ...
├─ downloaded-subtitles.zip
├─ another-subtitle-pack.7z
├─ loose-subtitle.ass
└─ unrelated downloads...
```

After processing, matched subtitles are created **directly beside the existing videos**:

```text
/storage/emulated/0/Download/Torrent/Some.Release/
├─ episode01.mkv        # untouched
├─ episode01.chs.ass    # created when needed
├─ episode01.cht.ass    # created when needed
├─ episode02.mkv        # untouched
└─ ...
```

## Safety contract

The Android port currently guarantees:

1. Video files are never moved, renamed, copied, deleted, or reorganized.
2. No `Video/`, `Subs/`, `SubBackup/`, or similar folder is created inside a torrent directory.
3. `Download/Torrent/**` is only the video-target discovery area.
4. Direct children of `Download` are the subtitle-source discovery area.
5. Source archives and loose source subtitles are retained after processing.
6. Existing destination subtitles are not overwritten.
7. Android SAF is used to authorize `Download`; the permission is persisted through a bookmark.
8. `SubRenamer.Core` remains responsible for episode-level matching.
9. Android-specific attribution only decides which subtitle source belongs to which torrent directory.
10. Undo never touches videos or source subtitles; it only removes unchanged files created by the most recent recorded Android batch.

For implementation details, see [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## User flow

1. On first use, choose `/storage/emulated/0/Download` through the Android folder picker.
2. Tap **扫描字幕与视频**.
3. The app discovers physical torrent directories that directly contain video files.
4. It discovers subtitle archives (`zip`, `7z`, `rar`) and loose subtitle files in the `Download` root.
5. It ranks likely `subtitle source → torrent target` relationships.
6. If attribution is not confident, select the correct torrent target manually.
7. The original `SubRenamer.Core` performs episode-level mapping.
8. Review the exact planned destination filenames.
9. Tap **确认处理 N 项** to create only the planned subtitle files.
10. If needed, use **撤销上次处理**. Files edited or replaced after creation are preserved rather than deleted.

A concrete example is in [`docs/WORKFLOW.md`](docs/WORKFLOW.md).

## What is implemented now

- SAF `Download` selection and bookmark restore.
- Recursive torrent-target discovery without flattening directories.
- Download-root subtitle discovery.
- ZIP / 7z / RAR listing and extraction through SharpCompress.
- Conservative work-level attribution with manual fallback.
- Typed direct call into the original `SubRenamer.Core`.
- One-to-many subtitle output with recognized language suffixes such as `chs` / `cht`.
- Preview and no-overwrite conflict protection.
- Background scanning / processing to avoid blocking the Android UI.
- Bounded concurrent folder/archive scanning and reduced repeated SAF enumeration.
- Safe one-level undo persisted across app restarts.
- Original upstream tests plus Android integration tests and CI-built installable APK artifacts.

The Android UI is still intentionally small. It does **not** yet expose the full desktop feature set. See [`docs/FEATURES.md`](docs/FEATURES.md) for an explicit comparison instead of inferring feature support from the desktop README.

## Important missing features

The largest functional gaps are not in the Core algorithm but in the Android shell around it:

- manual/regex matching modes and editors;
- per-item match correction / exclusion;
- language filtering and related subtitle-output settings;
- a real settings surface;
- subtitle synchronization through FFsubsync / FFmpeg.

These are prioritized in [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Build

Requirements:

- .NET 10 SDK + Android workload for the mobile app;
- .NET 8 SDK for the original upstream test project;
- JDK / Android SDK as required by the .NET Android workload.

```bash
dotnet workload install android

dotnet test SubRenamer.Tests/SubRenamer.Tests.csproj -c Release

dotnet test Android/tests/SubRenamer.Mobile.Tests/SubRenamer.Mobile.Tests.csproj -c Release

dotnet build Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj \
  -c Debug -f net10.0-android -t:SignAndroidPackage \
  -p:EmbedAssembliesIntoApk=true
```

The GitHub Actions workflow performs the test/build path and uploads a debug-signed APK artifact. CI also checks that the standalone APK actually contains the Android app assembly, mobile layer and original `SubRenamer.Core`; this prevents a Fast Deployment-style APK from being published as a standalone build.

See [`docs/VALIDATION.md`](docs/VALIDATION.md).

## Documentation

- [`../FORK.md`](../FORK.md) — fork-level overview and documentation policy.
- [`docs/FEATURES.md`](docs/FEATURES.md) — feature inventory and upstream comparison.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — prioritized remaining work.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — architecture and safety boundaries.
- [`docs/WORKFLOW.md`](docs/WORKFLOW.md) — end-to-end example.
- [`docs/VALIDATION.md`](docs/VALIDATION.md) — CI / APK validation.

## Upstream relationship

The original desktop application and its documentation remain upstream at [`qwqcode/SubRenamer`](https://github.com/qwqcode/SubRenamer). This fork keeps upstream desktop code and `SubRenamer.Core` available rather than rewriting their documentation as if every desktop feature already existed on Android.

## License

GPL-2.0. See the repository root [`LICENSE`](../LICENSE).
