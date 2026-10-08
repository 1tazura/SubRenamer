using System.ComponentModel;
using System.Runtime.CompilerServices;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Presentation;

public sealed class SourceCard : INotifyPropertyChanged
{
    private AttributionCandidate? _selectedCandidate;
    private string _state = "待处理";

    public required SubtitleSource Source { get; init; }
    public required IReadOnlyList<AttributionCandidate> Candidates { get; init; }

    public AttributionCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (Equals(_selectedCandidate, value)) return;
            _selectedCandidate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string Summary =>
        $"{Source.DisplayName}\n{Source.SubtitleCount} 字幕 · " +
        (SelectedCandidate is null ? "需要选择目标" : $"→ {SelectedCandidate.Target.RelativePath}") +
        $" · {State}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
