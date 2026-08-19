using System.Text.Json;

namespace SubRenamer.Mobile.Services;

public sealed record AppSettings(string? DownloadBookmark = null);

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SubRenamerMobile");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
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
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }
}
