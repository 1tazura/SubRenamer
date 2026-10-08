# Network storage: device probe and first safety increment

## Status

**Direct SMB scan/apply is not implemented.** The UI still uses Download/Torrent.
The independent-root scan overload and persisted undo identity are groundwork,
not an installable network-storage feature. No network media files were written
or downloaded during this probe.

Device clock at validation: 2026-10-09 (the date is the device's reported clock).
Baseline: `ac8dec5`, branch `android-port-v1`.

## Actual device observations

- Termux process: uid 10405; `su -c id` returned uid 0.
- Wi-Fi interface reported `192.168.3.8`; TCP connections to
  `192.168.1.128:445` and `:139` succeeded.
- Direct SMB negotiation reported dialect `0x311`, server GUID
  `766d6e61-7300-0000-0000-000000000000` and required signing.
- Samba `smbclient -L //192.168.1.128 -N` succeeded anonymously and listed
  `Anime`, **`Completed`**, and `IPC$`. Completed is a share, not merely a
  directory name. `smbclient //192.168.1.128/Completed -N -c 'ls'` succeeded.
- Completed contains work directories directly, including a Madoka grouping
  folder with nested series/movie directories, LOVE FLOPS, BAKI-DOU, Kaiji,
  and `ani-rss`. A nested Madoka series directory listed 12 MKV files.
  Only names/metadata were read. A second anonymous listing capped at SMB 2.1
  also succeeded, so the NAS is not restricted to SMB 3.1.1. No write permission
  was inferred from either listing.
- `pysmb` failed at dialect negotiation. Python smbprotocol negotiated SMB,
  but its empty-credential path did not perform an anonymous login, and a
  literal `guest`/empty-password login failed. These are **not** evidence that
  NAS anonymous access is disabled: the Samba client succeeded.
- `/proc/filesystems` did not list CIFS. No CIFS module was found in the
  inspected vendor module paths. A real read-only guest `mount -t cifs`
  attempt in `/data/local/tmp/subrenamer-cifs-probe` returned **No such device**.
  The empty probe mountpoint was removed. Mount visibility/application access
  could not be tested because there was no successful mount. APK root
  dependence is neither required nor added.

Repeat read-only commands (requires Termux Samba package):

```sh
smbclient -L //192.168.1.128 -N --option='client min protocol=SMB2'
smbclient //192.168.1.128/Completed -N -c 'ls' --option='client min protocol=SMB2'
```

If a future probe requires credentials, run smbclient interactively with `-U`
(username only), letting it prompt for the password. Never put passwords in
command arguments, code, logs, settings JSON, or Git.

## Implemented groundwork

- `ScanAllWithMetricsAsync(downloadRoot, videoRoot, token)` and
  `FindVideoTargetsInRootAsync(videoRoot, token)` discover videos directly in
  the explicit root and its descendants; no extra Torrent child is required.
  Download subtitle discovery and eager archive validation remain unchanged.
- Existing scan calls retain Download/Torrent and the single Download-root
  enumeration. Every discovered target now carries its authorized root URI.
- New undo records include `TargetRoot` (backend + root URI) and
  `TargetFolderUri`. The production generated JSON context retains these.
  Old records have null identities and remain Download-relative only.
- Undo validates root/backend before listing files, validates the recorded
  destination URI, and only reuses a live target handle when its URI matches.
  Unknown/SMB identities cannot fall back to a local Download path. Legacy
  records ignore an independent root or untrusted same-session target handle.
- Relative directory resolution rejects `.` / `..` segments.
- Apply refuses non-SAF identities. Generic `IStorageFolder.CreateFileAsync`
  does **not** guarantee SMB exclusive creation; this increment deliberately
  does not enable a network adapter through the existing SAF write pipeline.
- Both instrumented button handlers and older handlers record the new fields.

## Preservation and tests

Installed application: `io.github.subrenamer.mobile`, 0.1.24 / versionCode 25.
Installed signing certificate SHA-256:
`bcac1fd8848717282632cb736ef1264167dea7acbf2bdad5b4236a378163bbf9`.
Existing settings contain Download authorization and a 50-file undo batch.
Installed APK/settings/cache were copied to the private Termux directory
`~/subrenamer-preservation/` (directory mode 0700). This is a preservation
snapshot, not a tested Android restore procedure. No uninstall/data reset or
APK update was performed.

The device initially had no .NET SDK, JDK, workload, or SMB client. Termux
packages installed .NET SDK 8.0.131/10.0.112 and Samba. Actual device execution:

- Original tests: **52/52 passed**.
- Android managed-layer tests: **38/38 passed**, including 11 new tests for
  direct-root discovery, existing Torrent behavior, wrong root/backend/folder
  rejection, legacy isolation, path traversal rejection, and preventing SMB URIs
  from being silently labelled SAF. These are managed
  tests run on the phone, not APK/SAF device acceptance tests.
- Published self-contained **linux-bionic-arm64** trimmed persistence smoke:
  passed with JSON reflection disabled, including old-record defaults and
  explicit network backend/root/folder identity persistence across store restart.
  An existing Avalonia DesignerSupport IL2104 warning remains.
- Android workload installation was attempted but timed out before completion;
  no JDK/Android SDK was configured, no new APK was produced. No GitHub token
  was available in the inspected environment or standard credential files.

## Remaining network acceptance (not yet executed)

Choose a license-compatible, no-root managed SMB client; verify anonymous and
signed/authenticated connections on this NAS. Expose shared-folder/subdirectory
selection and persist a credential-free server/share/root identity (pin server
identity, not only an IP address). Restore it independently of Download.

SMB output creation must use server-side exclusive create, with stream/handle
ownership preserved through write, close, cleanup, and undo. Validate in an
isolated share directory: preview-to-create races, case collisions, disconnects,
partial-output cleanup/recovery, process restart, server/share/root changes,
modified output preservation, and SHA-256 undo. Do not use live media directories
for experiments. Neither anonymous write access nor any of these acceptance
properties has been tested yet.

Build/sign the eventual APK using a durable compatible key or an explicitly
validated data-preserving migration. Keep the PR Draft; do not merge main or
force-push. The current increment does not deliver the requested SMB placement
workflow or a replacement APK.
