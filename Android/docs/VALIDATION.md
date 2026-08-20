# Android validation contract

The Android port is validated in GitHub Actions rather than by assuming that a successful compile implies an installable standalone APK.

## CI layers

The Android workflow validates these layers in order:

1. **Original upstream tests**

   ```bash
   dotnet test SubRenamer.Tests/SubRenamer.Tests.csproj -c Release
   ```

   This protects the original matching behavior while the Android shell evolves.

2. **Android-shell integration tests**

   ```bash
   dotnet test Android/tests/SubRenamer.Mobile.Tests/SubRenamer.Mobile.Tests.csproj -c Release
   ```

   These cover Android-side helpers/integration such as the typed Core bridge and filename heuristics.

3. **Standalone Android APK build**

   ```bash
   dotnet build Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj \
     -c Debug -f net10.0-android -t:SignAndroidPackage \
     -p:EmbedAssembliesIntoApk=true
   ```

4. **APK payload inspection**

   CI lists the signed APK contents and requires the packaged arm64 payload to contain the app assembly, mobile layer and original Core assembly.

   This check exists because an earlier debug build could compile successfully yet behave like an IDE Fast Deployment package: managed assemblies were not embedded, so a sideloaded APK exited before Avalonia/managed application code could start.

5. **Artifact upload**

   CI publishes the debug-signed APK together with an APK content listing.

## Real-device milestones already reached

The Android branch has been exercised on a real Android device through the following failures and fixes:

- standalone APK initially exited before managed code because assemblies were not embedded;
- Activity startup then failed because the theme was not AppCompat-derived;
- SAF scanning caused Android ANR behavior when long storage work occupied the UI thread;
- those startup/execution issues were fixed and the basic end-to-end subtitle placement path has since been completed successfully on-device.

The current functional validation target is therefore beyond "opens successfully":

```text
authorize Download
  -> scan Torrent/video targets and Download subtitle sources
  -> attribute source to target
  -> run original SubRenamer.Core mapping
  -> preview
  -> create subtitles without touching videos
  -> undo the recorded created batch safely
```

## Safety properties to preserve in tests/review

CI cannot fully simulate every Android `DocumentsProvider`, so code review and real-device testing must continue to protect these invariants:

- no video move/rename/copy/delete operations;
- no automatic overwrite of existing subtitles;
- no deletion of source subtitle material;
- archive temporary files remain app-private;
- weak work attribution requires user choice;
- undo only deletes unchanged app-created outputs.

## Failure diagnostics

The workflow uploads Android test/build diagnostic logs when those stages fail. Normal successful runs only need the APK artifact and package listing.

## Release implication

A green CI run means the branch passed the repository's automated test/build/package checks. It does **not** by itself mean every Android feature is release-complete; current feature scope is tracked separately in [`FEATURES.md`](FEATURES.md) and [`ROADMAP.md`](ROADMAP.md).
