using NUnit.Framework;
using SubRenamer.Mobile.Models;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class ArchiveIndexCacheStoreTests
{
    private string _tempDir = null!;
    private string _cachePath = null!;
    private string _settingsPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SubRenamer-cache-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _cachePath = Path.Combine(_tempDir, "archive-index-cache.json");
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Test]
    public async Task Positive_and_negative_entries_round_trip()
    {
        var store = new ArchiveIndexCacheStore(_cachePath);
        var positive = new ArchiveIndexCacheRecord(
            "content://provider/document/positive.zip",
            123,
            456,
            [new SubtitleEntryRef("inside.ass", "inside.ass", ".ass", 42)]);
        var negative = new ArchiveIndexCacheRecord(
            "content://provider/document/negative.zip",
            789,
            987,
            []);

        await store.SaveAsync([positive, negative]);
        var loaded = await store.LoadAsync();

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded[positive.Identity].Entries, Has.Length.EqualTo(1));
        Assert.That(loaded[negative.Identity].Entries, Is.Empty);
    }

    [Test]
    public void Cache_record_requires_both_size_and_timestamp_to_match()
    {
        var record = new ArchiveIndexCacheRecord("id", 100, 200, []);

        Assert.That(record.Matches(100, 200), Is.True);
        Assert.That(record.Matches(101, 200), Is.False);
        Assert.That(record.Matches(100, 201), Is.False);
    }

    [Test]
    public async Task Changing_download_bookmark_clears_archive_cache()
    {
        var cache = new ArchiveIndexCacheStore(_cachePath);
        await cache.SaveAsync([
            new ArchiveIndexCacheRecord("archive", 100, 200, [])
        ]);

        var settings = new SettingsStore(_settingsPath, cache);
        await settings.SaveAsync(new AppSettings(DownloadBookmark: "bookmark-a"));
        await settings.SaveDownloadBookmarkAsync("bookmark-b");

        Assert.That(await cache.LoadAsync(), Is.Empty);
        Assert.That((await settings.LoadAsync()).DownloadBookmark, Is.EqualTo("bookmark-b"));
    }

    [Test]
    public async Task Reauthorizing_same_bookmark_keeps_archive_cache()
    {
        var cache = new ArchiveIndexCacheStore(_cachePath);
        await cache.SaveAsync([
            new ArchiveIndexCacheRecord("archive", 100, 200, [])
        ]);

        var settings = new SettingsStore(_settingsPath, cache);
        await settings.SaveAsync(new AppSettings(DownloadBookmark: "bookmark-a"));
        await settings.SaveDownloadBookmarkAsync("bookmark-a");

        Assert.That(await cache.LoadAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Archive_scan_count_text_includes_cache_hit_and_reindex_counts()
    {
        var summary = new ArchiveScanCount(25, 20, 5).ToString();

        Assert.That(summary, Does.Contain("25"));
        Assert.That(summary, Does.Contain("缓存命中 20"));
        Assert.That(summary, Does.Contain("实际重索引 5"));
    }
}
