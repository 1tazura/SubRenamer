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

   The workflow therefore keeps a dedicated **debug-only** keystore in the GitHub Actions cache and explicitly passes it through `AndroidSigningKeyStore` / `AndroidSigningKeyAlias` / password properties. The cache is keyed separately from build caches, and push/PR events for the same head branch share a concurrency group to avoid racing first-use keys. This preserves signing identity only while that cache survives: after eviction, a new key cannot update an APK signed with the old key. Compare the artifact's certificate with the installed channel before assuming in-place updates are possible. Durable signing-key management remains required for a stable distribution channel.

   This is only the sideload/testing signing channel. A future public release must use a separately managed private release keystore/secret, not this cached debug identity.

4. **Trimmed persistence smoke test**

   CI publishes and runs a self-contained Linux executable with `PublishTrimmed=true`, `TrimMode=partial` and reflection-based JSON disabled. It uses the production settings/cache stores to verify:

   - loading older settings JSON, including numeric matching modes and a persisted undo journal;
   - retaining optional-property defaults from bookmark-only settings;
   - saving rules and reopening the stores without losing the bookmark, output names or SHA-256 records;
   - loading older archive indexes and retaining positive, negative and stable-rejection records across restart;
   - invalidating undo and cache when the authorized root changes.

   Both stores use generated `JsonSerializerContext` metadata. The JSON schema remains compatible with older installations. The smoke test treats `IL2026`/`IL3050` as errors; the Android trimmed candidate also treats `IL2026` as an error.

   Source-card and target-candidate templates use typed compiled bindings instead of reflection bindings. Their displayed properties are checked by the XAML compiler and retained by direct references.

   This verifies managed persistence after trimming. It does not substitute for Android-device startup, SAF or archive-extraction tests.

5. **Standalone Android APK build**

   ```bash
   dotnet publish Android/src/SubRenamer.Mobile.Android/SubRenamer.Mobile.Android.csproj \
     -c Debug -f net10.0-android -r android-arm64 \
     -p:EmbedAssembliesIntoApk=true \
     -p:AndroidKeyStore=true \
     -p:AndroidSigningKeyStore=<stable-debug-keystore> \
     ...
   ```

6. **APK payload inspection**

   CI lists every produced APK's contents, requires the arm64 native payload and rejects an unexpected x86_64 payload. Publishing explicitly embeds managed assemblies for standalone sideloading.

   This check exists because an earlier debug build could compile successfully yet behave like an IDE Fast Deployment package: managed assemblies were not embedded, so a sideloaded APK exited before Avalonia/managed application code could start.

   The artifact also contains a text dump of the debug-signing certificate so signing identity changes are diagnosable.

7. **Artifact upload**

   CI publishes the arm64 Debug APK plus untrimmed/trimmed Release candidates when their probe builds succeed. All use the same debug-only signing identity. Content listings, size comparisons and signing-certificate information accompany the APKs. Release probes remain optional; a green workflow alone does not prove a Release candidate was produced.

   The last v0.1.24 probe produced all three candidates: Debug 40,363,640 bytes, untrimmed Release 35,273,513 bytes, trimmed Release 15,510,313 bytes (61.6% below Debug). These are build measurements, not a real-device acceptance of the trimmed package. Keep the stable Debug channel available until a candidate passes the device workflow below.

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

## Network candidate validation

The phone ran the original 52 tests and all 50 Android managed-layer tests,
including seven opt-in tests against an actual isolated loopback Samba share.
These exercise production scan/Core preview/apply/restart Undo, same-name/case
conflicts, disconnect/rollback, modified output retention and identity changes.
CI now builds the host native client and runs the same tests (without a fixture
the network tests are explicitly skipped). Trimmed persistence smoke additionally
checks network root/server/file identities via production generated JSON stores;
the published reflection-disabled linux-bionic-arm64 binary passed on-phone.

This is not APK/SAF or actual NAS acceptance. The NAS anonymously lists files
but denies creation of the isolated test directory. CI builds Android native
libraries, verifies their APK payloads, and produces a separate `networktest`
package so the old app's incompatible signing certificate/data remain intact.
See [`NETWORK_STORAGE.md`](NETWORK_STORAGE.md) for environment/probe/signature
observations and preservation details.

## Failure diagnostics

The workflow uploads Android test/build diagnostic logs when those stages fail. Normal successful runs only need the APK artifact, package listing and signing-certificate record.

## Release implication

A green CI run means the branch passed the repository's automated test/build/package checks. It does **not** by itself mean every Android feature is release-complete; current feature scope is tracked separately in [`FEATURES.md`](FEATURES.md) and [`ROADMAP.md`](ROADMAP.md).
