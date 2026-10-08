# Network video storage (v0.1.26 candidate)

## Scope and current acceptance

The application now has an independent video-root UI and an in-process,
non-root SMB2/3 backend. Subtitle sources remain direct children of authorized
Download, including eagerly validated ZIP/7z/RAR archives. Local
Download/Torrent remains selectable. A network root is scanned directly and
recursively: no Torrent child is required.

**This is a candidate, not completed NAS/device acceptance.** On this phone,
managed production scan/Core preview/apply/restart-Undo code passed against a
real, isolated loopback Samba server. This is stronger than mocks, but is not
APK/SAF acceptance or successful writing to the actual NAS. The NAS initially allowed
anonymous listing but denied isolated directory creation. After the user's
permission update, anonymous creation of the reserved isolated test directory
and two zero-byte test videos succeeded. Actual production NAS apply/Undo
acceptance is now in progress; ordinary media directories remain untouched.

## Device observations

Device clock at probing: 2026-10-09. Termux uid 10405; `su -c id` returned uid 0.
Wi-Fi address reported 192.168.3.8; TCP 192.168.1.128:445/139 succeeded.
Samba anonymous share enumeration listed Anime, **Completed**, and IPC$.
Completed is a share containing direct work directories and nested Madoka
series/movie directories. Only video names/metadata were read. SMB 3.1.1 and
an SMB 2.1-capped anonymous listing both succeeded. Server GUID reported by the
initial probe: `766d6e61-7300-0000-0000-000000000000`.

A read-only CIFS mount attempt returned **No such device**. CIFS was absent
from `/proc/filesystems` and the inspected vendor module paths. Mount visibility
and application access were not assumed. The APK contains no su/mount usage.
Root was used only for diagnostics and starting a disposable, **127.0.0.1-only**
SMB test server; the client and managed tests ran as the ordinary Termux uid.
The test server does not expose Download or real media directories.

## User flow

1. Authorize Download as before.
2. Open **设置网络视频根目录**.
3. Enter `smb://192.168.1.128/Completed` or a subdirectory URL. Enter a writable
   NAS account/password if anonymous access lacks the necessary permissions.
4. Connect/select the root. The app lists immediate child directories; choose
   one and reconnect to make it the selected recursive video root if desired.
5. Scan, attribute source to target, choose original Core Diff/Manual/Regex,
   review the exact output plan, then apply. Only planned subtitles are created.
6. Undo verifies content and network identity; unchanged app-created outputs
   are deleted, modified/replaced outputs are retained.

Passwords are masked in the UI, cleared from the text box when connecting,
and used only for the live session. No password or username is persisted in
settings, journal, source, logs, or Git. A restart tries anonymous access; if it
fails, the saved network root stays unavailable until credentials are entered.
The app never silently substitutes local Download paths for unavailable roots.

## Storage boundary and identity

Avalonia 12's storage interfaces are explicitly not user-implementable.
`IContentItem/File/Folder` form a small app-owned boundary. SAF wrappers delegate
all operations to authorized Avalonia handles (including bookmarks and fast
URI-derived names). `SmbStorageFolder/File` provide network metadata and
subtitle operations. The original Core matching algorithm is unchanged.

Settings persist a credential-free `VideoRoot` identity: canonical backend/root
URI, server GUID, volume serial and selected directory inode. Directory
URIs in plans/journals also include directory identity. Before network listing,
creation or deletion the backend rechecks root and target directory identities;
open operations recheck after acquiring a handle. Directory creation timestamps
are deliberately excluded: some Samba/filesystem combinations synthesize them
from mutable metadata, which caused the first Linux CI run to reject ordinary
directory changes. File identities are captured after transfer, not before it.
Restoring a different
server/share/root fails closed. Explicit reselection invalidates the previous
one-level journal without clearing the unrelated Download archive index.

Old settings/journals omit the new optional fields and retain Download-relative
semantics. They cannot be redirected through a newly selected network root.
New outputs record SHA-256 and a persistent inode/creation identity. All new
persistent properties use generated JSON metadata; no reflection serializer
was introduced. Unknown backend identities are rejected.

## Exclusive creation, rollback and Undo

The native boundary exposes no video write/move/rename, ordinary open-for-write,
truncate, overwrite or path-based unlink operations. A filename must have a
recognized subtitle extension before creation or network Undo.

- Output creation uses SMB **FILE_CREATE**, never OPEN_IF/OVERWRITE, with
  **share_access=0**. A server-side same-name/case collision fails, even when
  introduced after preview.
- A compound CREATE + SET_INFO marks the owned object delete-pending before
  returning a writable handle. It is closed/rolled back on transfer failure;
  a disconnected session leaves cleanup to the server.
- After full subtitle transfer/SHA, the client flushes, clears delete-pending,
  then closes. It does not blindly unlink a pathname on failure. If the commit
  reply/close is ambiguous, the result retains the known SHA/file identity for
  a subsequent restart Undo instead of pretending there was no output.
- Undo opens the recorded subtitle with READ+DELETE and share_access=0, hashes
  and checks file identity, then marks deletion **on that same handle**. Another
  writer/replacer cannot race between hash and a pathname-based delete.
- Reparse points/symlinks are not video candidates and are rejected on subtitle
  open. Traversal segments and credential-bearing URLs are rejected.

An important real-test finding: Samba treats CREATE's FILE_DELETE_ON_CLOSE
option as sticky; trying to clear it made supposedly committed outputs vanish.
The candidate instead uses clearable delete-pending SET_INFO in a compound
request. Do not regress to the superficially equivalent CREATE option.

This is not a distributed transaction. Server failure mid-compound or process
failure between a successful commit and journal persistence still requires
recovery work. No arbitrary remote object is deleted to hide an ambiguous
failure. Multi-batch/crash-proof transaction history is not claimed.

## Native build / licensing

libsmb2 is pinned to `fc710a3ebd58a3c15d0ef24322f748e38a3d3a90`, built as a
separate LGPL-2.1-or-later shared library. SubRenamer's narrow C ABI wrapper is
in `Android/native`. No Kerberos dependency is bundled. NDK arm64 builds target
Android API 23 and 16 KiB ELF page alignment. APKs include both native libraries
and license notices. Artifacts also provide the corresponding libsmb2 source
archive and build script/source via this repository. No SMB1 support is needed.

```sh
# host (Termux or Linux, requires CMake/Ninja/compiler)
bash Android/native/build.sh /private/native-output host
# Android (Linux CI with an installed NDK)
ANDROID_NDK_HOME=/path/to/ndk bash Android/native/build.sh Android/native/.build/android android
```

## Validation and signing preservation

On-phone runs passed original **52/52** tests and **50/50** Android managed tests.
The updated reflection-disabled trimmed persistence binary was also published
and executed on-phone, passing network root/server/file identity restoration
and legacy compatibility.
The latter include real loopback SMB root/nested scan/Core preview/apply, post-preview and
case collisions, rollback/disconnect cleanup, new connection/store restart
Undo, modified output retention, changed root/share/server rejection, and
identically hashed but externally replaced file retention, and refusal to
read/write/delete video bytes. CI runs the same loopback tests on Linux;
without an explicitly configured isolated fixture the seven network tests
are skipped, not falsely reported as exercised.

Installed original app: 0.1.24 / code 25. Its certificate SHA-256 is
`bcac1fd8848717282632cb736ef1264167dea7acbf2bdad5b4236a378163bbf9`.
The preserved APK/settings/cache are in private `~/subrenamer-preservation/`.
The current CI signing certificate is different:
`7e7af6d96d4f8beac165ffaf6f505fb3943375cfdb5d87d9f92ffc7083aa1dd1`.
It **cannot** update the old installed channel. Do not uninstall/clear data.

CI additionally builds `io.github.subrenamer.mobile.networktest`, labelled
**SubRenamer Network Test**, as a side-by-side package. It needs its own SAF
authorization and does not inherit the original app's undo journal. It is for
candidate acceptance, not a covert signing/data migration. The original app
and its data are left intact. The PR stays Draft; main is not merged.

Remaining acceptance: APK startup/native loading, Android SAF and archive
scan, writable NAS account, isolated NAS scan/apply/conflict/disconnect/restart
Undo, and controlled placement into normal media directories only after a
reviewed plan. A green build or loopback test does not substitute for these.
