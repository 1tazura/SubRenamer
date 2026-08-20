using NUnit.Framework;
using SubRenamer.Mobile.Services;

namespace SubRenamer.Mobile.Tests;

[TestFixture]
public sealed class StorageAccessServiceTests
{
    [TestCase(
        "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload%2FExample.zip",
        "Example.zip")]
    [TestCase(
        "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload%2FTorrent%2FShow%20Name",
        "Show Name")]
    [TestCase(
        "content://com.android.externalstorage.documents/tree/primary%3ADownload/document/primary%3ADownload",
        "Download")]
    public void External_storage_document_name_is_derived_without_metadata_query(string value, string expected)
    {
        var result = StorageAccessService.TryGetExternalStorageDocumentName(new Uri(value));
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Other_content_provider_falls_back()
    {
        var result = StorageAccessService.TryGetExternalStorageDocumentName(
            new Uri("content://com.example.provider/document/anything"));
        Assert.That(result, Is.Null);
    }
}
