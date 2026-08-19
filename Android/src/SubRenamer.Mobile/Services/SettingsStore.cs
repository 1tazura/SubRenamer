using System.Text.Json;

namespace SubRenamer.Mobile.Services;

public sealed record AppSettings(string? DownloadBookmark = null);

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore()
    {
        // Do not touch the filesystem while MainView is being constructed.
        // On Android, storage-backed special folders may not be ready until the
        // application/activity is fully initialized.
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Path.GetTempPath();

        _path = Path.Combine(baseDir, "SubRenamerMobile", "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }
}
