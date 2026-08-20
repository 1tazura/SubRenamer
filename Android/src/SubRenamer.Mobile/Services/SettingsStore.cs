using System.Text.Json;

namespace SubRenamer.Mobile.Services;

public sealed record UndoFileRecord(string DestinationName, string Sha256);

public sealed record UndoBatchRecord(
    string TargetRelativePath,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<UndoFileRecord> Files);

public sealed record AppSettings(
    string? DownloadBookmark = null,
    UndoBatchRecord? LastUndoBatch = null,
    CoreMatchMode MatchMode = CoreMatchMode.Diff,
    string ManualVideoPattern = "",
    string ManualSubtitlePattern = "",
    string VideoRegex = "",
    string SubtitleRegex = "");

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

    public async Task SaveDownloadBookmarkAsync(
        string bookmark,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        // A newly selected SAF root may point at a different Download tree.
        // Never carry an undo journal across roots.
        await SaveAsync(current with
        {
            DownloadBookmark = bookmark,
            LastUndoBatch = null,
        }, cancellationToken);
    }

    public async Task SaveUndoBatchAsync(
        UndoBatchRecord batch,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        await SaveAsync(current with { LastUndoBatch = batch }, cancellationToken);
    }

    public async Task ClearUndoBatchAsync(CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        if (current.LastUndoBatch is null)
            return;

        await SaveAsync(current with { LastUndoBatch = null }, cancellationToken);
    }

    public async Task SaveMatchSettingsAsync(
        CoreMatchMode mode,
        string manualVideoPattern,
        string manualSubtitlePattern,
        string videoRegex,
        string subtitleRegex,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        await SaveAsync(current with
        {
            MatchMode = mode,
            ManualVideoPattern = manualVideoPattern,
            ManualSubtitlePattern = manualSubtitlePattern,
            VideoRegex = videoRegex,
            SubtitleRegex = subtitleRegex,
        }, cancellationToken);
    }
}
