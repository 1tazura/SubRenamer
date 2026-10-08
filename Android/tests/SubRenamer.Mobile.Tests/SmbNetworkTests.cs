using System.Text;
using Avalonia.Platform.Storage;
using NUnit.Framework;
using SubRenamer.Mobile.Models;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
[NonParallelizable]
public sealed class SmbNetworkTests
{
    private string _directory = "", _url = "";
    [SetUp]
    public void SetUp()
    {
        var url = Environment.GetEnvironmentVariable("SUBRENAMER_SMB_TEST_URL");
        var local = Environment.GetEnvironmentVariable("SUBRENAMER_SMB_TEST_LOCAL_DIR");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(local))
            Assert.Ignore("Opt-in loopback SMB fixture is not configured.");
        var name = "test_" + Guid.NewGuid().ToString("N");
        _directory = Path.Combine(local!, name);
        Directory.CreateDirectory(_directory);
        _url = url!.TrimEnd('/') + "/" + name;
        // Isolated test-only zero-byte videos. The SMB app must never read them.
        File.WriteAllBytes(Path.Combine(_directory, "ShowE01.mkv"), []);
        File.WriteAllBytes(Path.Combine(_directory, "ShowE02.mkv"), []);
    }
    [TearDown]
    public void TearDown()
    { if (_directory.Length > 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private async Task<MatchPlan> Plan(SmbConnection connection, string relative = "")
    {
        var targets = await new ScanService(new ArchiveService()).FindVideoTargetsInRootAsync(connection.Root);
        var target = targets.Single(t => t.RelativePath == relative);
        Assert.That(target.VideoCount, Is.EqualTo(2));
        var files = new[] { new MemorySubtitle("SubE01.ass", "subtitle 01"), new MemorySubtitle("SubE02.ass", "subtitle 02") };
        var source = new SubtitleSource(SubtitleSourceKind.LooseGroup, "test subtitles", null, files,
            files.Select(f => new SubtitleEntryRef(f.Name, f.Name, ".ass")).ToArray());
        return await new PlanBuilder(new SubRenamerCoreBridge()).BuildAsync(source, target,
            new CoreMatchSettings(CoreMatchMode.Regex, @"E(\d+)", @"E(\d+)"));
    }

    [Test]
    public async Task Scan_Core_preview_apply_and_restart_undo_preserve_modified_output()
    {
        StorageRootIdentity identity;
        var settingsPath = Path.Combine(_directory, "journal.json");
        var store = new SettingsStore(settingsPath);
        using (var connection = await SmbConnection.ConnectAsync(_url))
        {
            identity = connection.RootIdentity;
            var plan = await Plan(connection);
            Assert.That(plan.ReadyCount, Is.EqualTo(2));
            var applied = await new ApplyService(new ArchiveService()).ApplyAsync(plan);
            Assert.That(applied.Errors, Is.Empty);
            Assert.That(applied.Applied, Is.EqualTo(2));
            await store.SaveUndoBatchAsync(new UndoBatchRecord(plan.Target.RelativePath, DateTimeOffset.UtcNow,
                applied.CreatedFiles.Select(f => new UndoFileRecord(f.DestinationName, f.Sha256, f.FileIdentity)).ToArray(),
                plan.Target.RootIdentity, plan.Target.Folder.Path.AbsoluteUri));
            var duplicate = await new ApplyService(new ArchiveService()).ApplyAsync(plan);
            Assert.That(duplicate.Applied, Is.Zero);
            Assert.That(duplicate.Skipped, Is.EqualTo(2));
        }
        File.Delete(Path.Combine(_directory, "ShowE02.ass"));
        File.WriteAllText(Path.Combine(_directory, "ShowE02.ass"), "externally changed");
        using var restored = await SmbConnection.ConnectAsync(_url, expectedIdentity: identity);
        var batch = (await new SettingsStore(settingsPath).LoadAsync()).LastUndoBatch!;
        var undone = await new UndoService(store).UndoLastAsync(restored.Root, batch, videoRoot: restored.Root);
        Assert.That(undone.Errors, Is.Empty);
        Assert.That(undone.Deleted, Is.EqualTo(1));
        Assert.That(undone.Changed, Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(_directory, "ShowE02.ass")), Is.EqualTo("externally changed"));
        Assert.That(File.Exists(Path.Combine(_directory, "ShowE01.mkv")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "ShowE02.mkv")), Is.True);
    }

    [Test]
    public async Task Nested_video_directory_apply_and_restart_resolution_work_without_Torrent()
    {
        var nested = Path.Combine(_directory, "Season1");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "ShowE01.mkv"), []);
        File.WriteAllBytes(Path.Combine(nested, "ShowE02.mkv"), []);
        UndoBatchRecord journal;
        StorageRootIdentity identity;
        using (var connection = await SmbConnection.ConnectAsync(_url))
        {
            identity = connection.RootIdentity;
            var plan = await Plan(connection, "Season1");
            var applied = await new ApplyService(new ArchiveService()).ApplyAsync(plan);
            Assert.That(applied.Errors, Is.Empty);
            Assert.That(applied.Applied, Is.EqualTo(2));
            journal = new UndoBatchRecord("Season1", DateTimeOffset.UtcNow,
                applied.CreatedFiles.Select(f => new UndoFileRecord(f.DestinationName, f.Sha256, f.FileIdentity)).ToArray(),
                identity, plan.Target.Folder.Path.AbsoluteUri);
        }
        using var restored = await SmbConnection.ConnectAsync(_url, expectedIdentity: identity);
        var result = await new UndoService(new SettingsStore(Path.Combine(_directory, "journal.json")))
            .UndoLastAsync(restored.Root, journal, videoRoot: restored.Root);
        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Deleted, Is.EqualTo(2));
    }

    [Test]
    public async Task Identical_bytes_in_an_externally_replaced_file_are_preserved_by_file_identity()
    {
        using var connection = await SmbConnection.ConnectAsync(_url);
        var output = connection.Root.CreateSubtitleExclusive("identity.ass");
        output.Write(Encoding.UTF8.GetBytes("same bytes"));
        var identity = output.FileIdentity;
        output.Commit();
        File.Delete(Path.Combine(_directory, "identity.ass"));
        File.WriteAllText(Path.Combine(_directory, "identity.ass"), "same bytes");
        var files = await StorageAccessService.SnapshotChildFilesAsync(connection.Root);
        await using var input = await files["identity.ass"].OpenReadAsync();
        var fingerprint = await StreamCopyService.ComputeSha256Async(input);
        await input.DisposeAsync();
        var result = await ((SmbStorageFile)files["identity.ass"]).DeleteIfUnchangedAsync(fingerprint.Sha256, identity, default);
        Assert.That(result.Deleted, Is.False);
        Assert.That(File.Exists(Path.Combine(_directory, "identity.ass")), Is.True);
    }

    [Test]
    public async Task Server_exclusive_create_rejects_collision_after_preview_and_case_variant()
    {
        using var connection = await SmbConnection.ConnectAsync(_url);
        var plan = await Plan(connection);
        File.WriteAllText(Path.Combine(_directory, "ShowE01.ass"), "pre-existing");
        var applied = await new ApplyService(new ArchiveService()).ApplyAsync(plan);
        Assert.That(applied.Applied, Is.EqualTo(1));
        Assert.That(applied.Skipped, Is.EqualTo(1));
        using var other = await SmbConnection.ConnectAsync(_url);
        var error = Assert.Throws<SmbException>(() => other.Root.CreateSubtitleExclusive("showe01.ASS"));
        Assert.That(error!.Code, Is.EqualTo(-17));
        Assert.That(File.ReadAllText(Path.Combine(_directory, "ShowE01.ass")), Is.EqualTo("pre-existing"));
    }

    [Test]
    public async Task Uncommitted_output_is_removed_on_close_and_disconnect()
    {
        var connection = await SmbConnection.ConnectAsync(_url);
        using (var uncommitted = connection.Root.CreateSubtitleExclusive("rollback.ass"))
            uncommitted.Write(Encoding.UTF8.GetBytes("partial"));
        Assert.That(File.Exists(Path.Combine(_directory, "rollback.ass")), Is.False);
        var interrupted = connection.Root.CreateSubtitleExclusive("disconnect.ass");
        interrupted.Write(Encoding.UTF8.GetBytes("partial"));
        connection.BreakConnectionForTest();
        try { interrupted.Dispose(); } catch (IOException) { }
        connection.Dispose();
        for (var attempt = 0; attempt < 30 && File.Exists(Path.Combine(_directory, "disconnect.ass")); attempt++)
            await Task.Delay(100);
        Assert.That(File.Exists(Path.Combine(_directory, "disconnect.ass")), Is.False);
    }

    [Test]
    public async Task Changed_root_or_share_identity_cannot_restore_or_undo()
    {
        StorageRootIdentity identity;
        using (var connection = await SmbConnection.ConnectAsync(_url)) identity = connection.RootIdentity;
        Assert.ThrowsAsync<IOException>(async () => await SmbConnection.ConnectAsync(_url,
            expectedIdentity: identity with { ServerIdentity = "different-server" }));
        Assert.ThrowsAsync<IOException>(async () => await SmbConnection.ConnectAsync(_url,
            expectedIdentity: identity with { RootUri = identity.RootUri + "/other" }));
        var oldDirectory = _directory + "_old";
        Directory.Move(_directory, oldDirectory);
        Directory.CreateDirectory(_directory);
        try
        {
            Assert.ThrowsAsync<IOException>(async () => await SmbConnection.ConnectAsync(_url, expectedIdentity: identity));
        }
        finally { Directory.Delete(_directory); Directory.Move(oldDirectory, _directory); }
    }

    [Test]
    public async Task Existing_video_write_and_path_delete_are_not_exposed()
    {
        using var connection = await SmbConnection.ConnectAsync(_url);
        var files = await StorageAccessService.SnapshotChildFilesAsync(connection.Root);
        Assert.ThrowsAsync<NotSupportedException>(async () => await files["ShowE01.mkv"].OpenReadAsync());
        Assert.ThrowsAsync<NotSupportedException>(async () => await files["ShowE01.mkv"].OpenWriteAsync());
        Assert.ThrowsAsync<NotSupportedException>(async () => await files["ShowE01.mkv"].DeleteAsync());
        Assert.Throws<NotSupportedException>(() => connection.Root.CreateSubtitleExclusive("overwrite.mkv"));
    }

    private sealed class MemorySubtitle(string name, string content) : IContentFile
    {
        public string Name => name;
        public Uri Path => new("file:///private-source/" + name);
        public bool CanBookmark => false;
        public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
        public Task<Stream> OpenWriteAsync() => throw new NotSupportedException();
        public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties(null, null, null));
        public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);
        public Task<IContentFolder?> GetParentAsync() => Task.FromResult<IContentFolder?>(null);
        public Task DeleteAsync() => throw new NotSupportedException();
        public Task<IContentItem?> MoveAsync(IContentFolder destination) => throw new NotSupportedException();
        public void Dispose() { }
    }
}

[TestFixture]
public sealed class SmbLocationTests
{
    [TestCase("smb://host/share/../other")]
    [TestCase("smb://user:password@host/share")]
    [TestCase("smb://host/share/%2Fother")]
    [TestCase("smb://host/share?password=x")]
    public void Unsafe_locations_are_rejected(string input)
        => Assert.Throws<ArgumentException>(() => SmbLocation.Parse(input));
    [Test]
    public void Unicode_subdirectory_and_UNC_location_are_supported()
        => Assert.That(SmbLocation.Parse(@"\\192.168.1.128\Completed\字幕测试").Directory, Is.EqualTo("字幕测试"));
}
