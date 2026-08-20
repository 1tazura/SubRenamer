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

3. **Stable CI debug signing identity**

   GitHub-hosted runners are ephemeral. If `SignAndroidPackage` is allowed to create its default debug key independently on every runner, successive APKs have different signing certificates and Android refuses an in-place update even when the package name and versionCode are correct.

   The workflow therefore keeps a dedicated **debug-only** keystore in the GitHub Actions cache and explicitly passes it through `AndroidSigningKeyStore` / `AndroidSigningKeyAlias` / password properties. The cache is keyed separately from build caches, and workflow concurrency prevents several fresh runs from racing to establish different first-use keys.

   This is only the sideload/testing signing channel. A future public release must use a separately managed private release keystore/secret, not this cached debug identity.

4. **Standalone Android APK build**

   ```bash
   dotnet build Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj \
     -c Debug -f net10.0-android -t:SignAndroidPackage \
     -p:EmbedAssembliesIntoApk=true \
     -p:AndroidKeyStore=true \
     -p:AndroidSigningKeyStore=<stable-debug-keystore> \
     ...
   ```

5. **APK payload inspection**

   CI lists the signed APK contents and requires the packaged arm64 payload to contain the app assembly, mobile layer and original Core assembly.

   This check exists because an earlier debug build could compile successfully yet behave like an IDE Fast Deployment package: managed assemblies were not embedded, so a sideloaded APK exited before Avalonia/managed application code could start.

   The artifact also contains a text dump of the debug-signing certificate so signing identity changes are diagnosable.

6. **Artifact upload**

   CI publishes the debug-signed APK together with an APK content listing and debug signing-certificate information.

## Real-device milestones already reached

The Android branch has been exercised on a real Android device through the following failures and fixes:

- standalone APK initially exited before managed code because assemblies were not embedded;
- Activity startup then failed because the theme was not AppCompat-derived;
- SAF scanning caused Android ANR behavior when long storage work occupied the UI thread;
- successive CI builds initially required uninstall/reinstall because ephemeral runners generated different default debug signing keys;
- those startup/execution/signing issues were fixed and the basic end-to-end subtitle placement path has since been completed successfully on-device.

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

The workflow uploads Android test/build diagnostic logs when those stages fail. Normal successful runs only need the APK artifact, package listing and signing-certificate record.

## Release implication

A green CI run means the branch passed the repository's automated test/build/package checks. It does **not** by itself mean every Android feature is release-complete; current feature scope is tracked separately in [`FEATURES.md`](FEATURES.md) and [`ROADMAP.md`](ROADMAP.md).
