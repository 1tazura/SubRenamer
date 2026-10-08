using System.Text.Json;
using SubRenamer.Mobile.Models;
using SubRenamer.Mobile.Services;

// Run the published binary, rather than a test host that keeps reflection
// metadata alive. Exercise the production stores using both old JSON and new
// writes, including the undo data required after an app restart.
if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("The smoke test must disable reflection-based JSON.");

var directory = Path.Combine(Path.GetTempPath(), "SubRenamer-trim-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var settingsPath = Path.Combine(directory, "settings.json");
    var cachePath = Path.Combine(directory, "archive-index-cache.json");
    var cache = new ArchiveIndexCacheStore(cachePath);
    var settings = new SettingsStore(settingsPath, cache);

    // Pre-source-generation format: enums are numbers and undo files are an
    // IReadOnlyList. A changed serializer must retain both across upgrades.
    await File.WriteAllTextAsync(settingsPath, """
        {
          "DownloadBookmark": "content://provider/Download",
          "LastUndoBatch": {
            "TargetRelativePath": "Torrent/测试剧集",
            "CreatedAtUtc": "2026-08-20T17:00:00+00:00",
            "Files": [
              { "DestinationName": "episode01.chs.ass", "Sha256": "ABC123" },
              { "DestinationName": "episode01.cht.ass", "Sha256": "DEF456" }
            ]
          },
          "MatchMode": 2,
          "ManualVideoPattern": "Show - $$ *.mkv",
          "ManualSubtitlePattern": "[Sub] Show $$ *.ass",
          "VideoRegex": "E(\\d+)",
          "SubtitleRegex": "show\\.(\\d+)"
        }
        """);

    var old = await settings.LoadAsync();
    Check(old.DownloadBookmark == "content://provider/Download", "legacy SAF bookmark");
    Check(old.MatchMode == CoreMatchMode.Regex && old.VideoRegex == @"E(\d+)", "legacy matching mode/rules");
    Check(old.ManualVideoPattern == "Show - $$ *.mkv" && old.SubtitleRegex == @"show\.(\d+)", "inactive rules");
    Check(old.LastUndoBatch is not null, "legacy undo batch");
    Check(old.LastUndoBatch!.Files.SequenceEqual([
        new UndoFileRecord("episode01.chs.ass", "ABC123"),
        new UndoFileRecord("episode01.cht.ass", "DEF456")]), "legacy undo files/hashes");
    Check(old.LastUndoBatch.TargetRelativePath == "Torrent/测试剧集" &&
          old.LastUndoBatch.CreatedAtUtc == DateTimeOffset.Parse("2026-08-20T17:00:00+00:00"), "legacy undo location/time");

    await settings.SaveMatchSettingsAsync(CoreMatchMode.Manual, "video $$", "sub $$", "video (.+)", "sub (.+)");
    var restarted = new SettingsStore(settingsPath, cache);
    var saved = await restarted.LoadAsync();
    Check(saved.MatchMode == CoreMatchMode.Manual && saved.ManualVideoPattern == "video $$" &&
          saved.ManualSubtitlePattern == "sub $$" && saved.VideoRegex == "video (.+)" &&
          saved.SubtitleRegex == "sub (.+)", "saved rules after restart");
    Check(saved.DownloadBookmark == old.DownloadBookmark &&
          saved.LastUndoBatch?.Files.SequenceEqual(old.LastUndoBatch.Files) == true &&
          saved.LastUndoBatch.TargetRelativePath == old.LastUndoBatch.TargetRelativePath &&
          saved.LastUndoBatch.CreatedAtUtc == old.LastUndoBatch.CreatedAtUtc, "rule updates preserve bookmark/undo");

    Check(saved.LastUndoBatch!.TargetRoot is null && saved.LastUndoBatch.TargetFolderUri is null,
        "legacy undo remains Download-relative");

    var explicitRoot = new StorageRootIdentity("smb", "smb://192.168.1.128/Completed/作品", "server-guid", "root-inode:birth");
    await restarted.SaveVideoRootAsync(explicitRoot);
    await restarted.SaveUndoBatchAsync(old.LastUndoBatch with
    {
        TargetRoot = explicitRoot,
        TargetFolderUri = "smb://192.168.1.128/Completed/作品/Season1",
        Files = [new UndoFileRecord("episode01.ass", "ABC123", "file-inode:birth")],
    });
    var explicitSaved = await new SettingsStore(settingsPath, cache).LoadAsync();
    Check(explicitSaved.VideoRoot == explicitRoot && explicitSaved.LastUndoBatch?.TargetRoot == explicitRoot &&
          explicitSaved.LastUndoBatch.Files.Single().FileIdentity == "file-inode:birth" &&
          explicitSaved.LastUndoBatch.TargetFolderUri == "smb://192.168.1.128/Completed/作品/Season1",
        "explicit backend/root/folder identity after restart");

    // Earliest settings only contained a bookmark. Missing optional properties
    // must retain the record's constructor defaults, rather than CLR nulls.
    await File.WriteAllTextAsync(settingsPath, """{"DownloadBookmark":"legacy-bookmark"}""");
    Check(await settings.LoadAsync() == new AppSettings(DownloadBookmark: "legacy-bookmark"), "missing-property defaults");

    // Older cache records predate StableRejectionError. Preserve their full
    // subtitle list so eager-validation cache hits remain meaningful.
    await File.WriteAllTextAsync(cachePath, """
        [{"Identity":"positive","Size":123,"ModifiedUtcTicks":456,
          "Entries":[{"Key":"字幕/01.ass","DisplayName":"01.ass","Extension":".ass","Size":42}]}]
        """);
    var oldCache = await cache.LoadAsync();
    Check(oldCache.Count == 1 && oldCache["positive"].Matches(123, 456), "legacy cache identity/signature");
    Check(oldCache["positive"].Entries.SequenceEqual([
        new SubtitleEntryRef("字幕/01.ass", "01.ass", ".ass", 42)]) &&
          !oldCache["positive"].IsStableRejection, "legacy cache entries/defaults");

    await cache.SaveAsync([
        oldCache["positive"],
        new ArchiveIndexCacheRecord("negative", 200, 300, []),
        new ArchiveIndexCacheRecord("rejected", 400, 500, [], "Cannot determine compressed stream type.")]);
    var reloaded = await new ArchiveIndexCacheStore(cachePath).LoadAsync();
    Check(reloaded.Count == 3 && reloaded["positive"].Entries.SequenceEqual(oldCache["positive"].Entries), "cache restart");
    Check(reloaded["negative"].Entries.Length == 0 && !reloaded["negative"].IsStableRejection &&
          reloaded["rejected"].IsStableRejection &&
          reloaded["rejected"].StableRejectionError == "Cannot determine compressed stream type.", "negative/rejected cache restart");

    // Changing the authorized root still invalidates cache and undo; nothing
    // from the previous provider/tree may be carried into the new tree.
    await restarted.SaveUndoBatchAsync(old.LastUndoBatch);
    await restarted.SaveDownloadBookmarkAsync("new-bookmark");
    var changedRoot = await restarted.LoadAsync();
    Check(changedRoot.DownloadBookmark == "new-bookmark" && changedRoot.LastUndoBatch is null, "root change clears undo");
    Check((await cache.LoadAsync()).Count == 0, "root change clears cache");

    Console.WriteLine("Persistence smoke passed: legacy upgrade, settings/undo restart, archive cache, root invalidation; JSON reflection disabled.");
}
finally
{
    Directory.Delete(directory, true);
}

static void Check(bool condition, string scenario)
{
    if (!condition)
        throw new InvalidOperationException($"Trimmed persistence failed: {scenario}");
}
