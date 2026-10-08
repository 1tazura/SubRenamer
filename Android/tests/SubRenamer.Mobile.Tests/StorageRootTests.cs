using System.Reflection;
using IStorageItem = SubRenamer.Mobile.Services.IContentItem;
using IStorageFile = SubRenamer.Mobile.Services.IContentFile;
using IStorageFolder = SubRenamer.Mobile.Services.IContentFolder;
using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class StorageRootTests
{
    [Test]
    public void Network_URI_cannot_be_silently_labelled_as_SAF()
    {
        var uri = new Uri("smb://192.168.1.128/Completed");
        Assert.Throws<NotSupportedException>(() => StorageRootIdentity.ForSaf(uri));
        Assert.That(new StorageRootIdentity("saf", uri.AbsoluteUri).MatchesSaf(uri), Is.False);
    }

    [Test]
    public async Task Explicit_video_root_is_scanned_directly_including_nested_folders()
    {
        var video = Item<IStorageFile>("file:///share/Season1/01.mkv");
        var nested = Folder("file:///share/Season1", video);
        var rootVideo = Item<IStorageFile>("file:///share/movie.mp4");
        var root = Folder("file:///share", nested, rootVideo);
        var targets = await new ScanService(new ArchiveService()).FindVideoTargetsInRootAsync(root);
        Assert.That(targets.Select(x => x.RelativePath), Is.EquivalentTo(new[] { "", "Season1" }));
        Assert.That(targets.All(x => x.RootIdentity == StorageRootIdentity.ForSaf(root.Path)), Is.True);
        Assert.That(targets.Sum(x => x.VideoCount), Is.EqualTo(2));
    }

    [Test]
    public async Task Existing_Download_Torrent_workflow_keeps_Download_identity()
    {
        var movie = Item<IStorageFile>("file:///Download/Torrent/Work/01.mkv");
        var work = Folder("file:///Download/Torrent/Work", movie);
        var torrent = Folder("file:///Download/Torrent", work);
        var download = Folder("file:///Download", torrent);
        var targets = await new ScanService(new ArchiveService()).FindVideoTargetsAsync(download);
        Assert.That(targets.Single().RelativePath, Is.EqualTo("Torrent/Work"));
        Assert.That(targets.Single().RootIdentity, Is.EqualTo(StorageRootIdentity.ForSaf(download.Path)));
    }

    [TestCase("smb", "smb://192.168.1.128/Completed")]
    [TestCase("unknown", "file:///Download")]
    [TestCase("saf", "file:///other-root")]
    public void Undo_rejects_other_backend_or_root_before_enumeration(string backend, string uri)
    {
        var download = Folder("file:///Download");
        var batch = new UndoBatchRecord("Torrent/Work", DateTimeOffset.UtcNow, [],
            new StorageRootIdentity(backend, uri), "file:///Download/Torrent/Work");
        var target = Folder("file:///Download/Torrent/Work");
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new UndoService(new SettingsStore()).UndoLastAsync(download, batch, resolvedTarget: target));
        Assert.That(Proxy(download).Enumerations, Is.Zero);
        Assert.That(Proxy(target).Enumerations, Is.Zero);
    }

    [Test]
    public void Undo_rejects_changed_target_identity_before_file_snapshot()
    {
        var target = Folder("file:///Download/Torrent/Work");
        var torrent = Folder("file:///Download/Torrent", target);
        var download = Folder("file:///Download", torrent);
        var batch = new UndoBatchRecord("Torrent/Work", DateTimeOffset.UtcNow, [],
            StorageRootIdentity.ForSaf(download.Path), "file:///Download/Torrent/Other");
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new UndoService(new SettingsStore()).UndoLastAsync(download, batch, resolvedTarget: target));
        Assert.That(Proxy(target).Enumerations, Is.Zero);
    }

    [Test]
    public async Task Legacy_undo_never_uses_independent_video_root_or_untrusted_handle()
    {
        var download = Folder("file:///Download");
        var external = Folder("file:///share");
        var batch = new UndoBatchRecord("Torrent/Work", DateTimeOffset.UtcNow, []);
        var result = await new UndoService(new SettingsStore()).UndoLastAsync(
            download, batch, resolvedTarget: external, videoRoot: external);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(Proxy(download).Enumerations, Is.EqualTo(1));
        Assert.That(Proxy(external).Enumerations, Is.Zero);
    }

    [TestCase("../Work")]
    [TestCase("Torrent/../Work")]
    [TestCase("Torrent/./Work")]
    public void Relative_resolution_rejects_traversal_before_enumeration(string path)
    {
        var root = Folder("file:///Download");
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await StorageAccessService.ResolveRelativeFolderAsync(root, path));
        Assert.That(Proxy(root).Enumerations, Is.Zero);
    }

    private static IStorageFolder Folder(string path, params IStorageItem[] children)
    {
        var folder = Item<IStorageFolder>(path);
        Proxy(folder).Children = children;
        return folder;
    }

    private static T Item<T>(string path) where T : class, IStorageItem
    {
        var item = DispatchProxy.Create<T, StorageProxy>();
        Proxy(item).Uri = new Uri(path);
        return item;
    }

    private static StorageProxy Proxy(IStorageItem item) => (StorageProxy)(object)item;

    // Unexpected operations (open/write/delete/move) fail the test. Scanning
    // must use only file names and folder listings, never video bytes.
    public class StorageProxy : DispatchProxy
    {
        public Uri Uri { get; set; } = null!;
        public IStorageItem[] Children { get; set; } = [];
        public int Enumerations { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "get_Path": return Uri;
                case "get_Name": return Uri.UnescapeDataString(Uri.AbsolutePath.Split('/').Last());
                case "GetItemsAsync": Enumerations++; return Items();
                default: throw new InvalidOperationException($"Unexpected storage call: {method?.Name}");
            }
        }

        private async IAsyncEnumerable<IStorageItem> Items()
        {
            await Task.CompletedTask;
            foreach (var child in Children) yield return child;
        }
    }
}
