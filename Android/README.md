# SubRenamer Android — torrent-safe subtitle placer

Android adaptation project built around the original **SubRenamer.Core** matching algorithm.

## Frozen v1 invariants

The application is intentionally specialized for this storage layout:

```text
/storage/emulated/0/Download/
├─ Torrent/
│  ├─ Some.Torrent.Release/
│  │  ├─ episode01.mkv
│  │  ├─ episode02.mkv
│  │  └─ ...
│  └─ Another.Release/
│     └─ ...
├─ downloaded-subtitles.zip
├─ another-subtitle-pack.7z
└─ unrelated downloads...
```

After a successful operation, matched subtitles are written **directly beside the existing video files**:

```text
/storage/emulated/0/Download/Torrent/Some.Torrent.Release/
├─ episode01.mkv
├─ episode01.ass
├─ episode02.mkv
├─ episode02.ass
└─ ...
```

Hard guarantees in v1:

1. **Video files are never moved, renamed, copied, deleted, or reorganized.**
2. No `Video/`, `Subs/`, `SubBackup/`, or other folder is created inside a torrent directory.
3. `/Download/Torrent/**` is only the video-target discovery area.
4. Direct children of `/Download/` are the subtitle-source area.
5. Subtitle archives/source files are retained after success.
6. Existing destination subtitles are not overwritten; they are reported as conflicts.
7. A root directory is authorized once through Android SAF and reopened through an Avalonia bookmark.
8. The original SubRenamer Core remains responsible for episode-level `diff → extract → mapping`.
9. The Android-specific code only decides which subtitle source belongs to which torrent directory, then applies the Core result.

## User flow

1. On first launch, tap **选择 Download 目录** and authorize `/storage/emulated/0/Download`.
2. The app remembers that folder through a persistent storage bookmark.
3. It finds torrent directories that contain video files directly.
4. It inspects subtitle archives (`zip`, `7z`, `rar`) and loose subtitle files in the `Download` root.
5. It ranks likely torrent-directory targets from title/release/season evidence.
6. If attribution is confident, the target is preselected. If not, choose one target from the list.
7. SubRenamer.Core performs episode-level matching.
8. Preview shows exact `subtitle → destination subtitle` operations.
9. Tap **应用**. Only subtitle bytes are written into the existing torrent directory.

## Core integration

This fork keeps the original `SubRenamer.Core` project unchanged and references it directly from the Android shell.
`SubRenamerCoreBridge` is a thin typed adapter around `Matcher.Execute`; the Android code does not reimplement episode matching.

## Build

Requirements:

- .NET 10 SDK + Android workload for the mobile app
- .NET 8 SDK for the upstream SubRenamer test project
- JDK/Android SDK as required by the .NET Android workload

```bash
dotnet workload install android
dotnet test SubRenamer.Tests/SubRenamer.Tests.csproj -c Release
dotnet test Android/tests/SubRenamer.Mobile.Tests/SubRenamer.Mobile.Tests.csproj -c Release
dotnet build Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj \
  -c Debug -f net10.0-android -t:SignAndroidPackage
```

GitHub Actions performs these steps and uploads an installable debug-signed APK artifact.

## Current implementation scope

Implemented in the source tree:

- SAF root selection + bookmark restore
- Torrent target discovery without flattening directories
- Download-root subtitle source discovery
- ZIP / 7z / RAR content listing and extraction through SharpCompress
- Series/work-level attribution with conservative ambiguity handling
- Typed bridge to the fork's original `SubRenamer.Core`
- Android-side integration tests plus the original upstream test suite
- Preview plan generation
- Conflict-safe subtitle placement
- Minimal Android UI

Deliberately not included in v1:

- moving or renaming videos
- generic file browser
- subtitle synchronization / FFsubsync / FFmpeg
- regular-expression editor
- destructive source cleanup
- automatic overwrite

## Build-status note

This source bundle was generated in an environment without a .NET SDK or Android SDK, so no local APK compile was claimed. The included CI workflow is the executable build/test path and deliberately runs the original test suite before producing the APK.

## License

GPL-2.0. The fork uses the repository root `LICENSE`.
