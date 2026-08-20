using System.ComponentModel;
using System.Runtime.CompilerServices;
using SubRenamer.Mobile.Models;

namespace SubRenamer.Mobile.Presentation;

public sealed class SourceCard : INotifyPropertyChanged
{
    private SubtitleSource _source = null!;
    private IReadOnlyList<AttributionCandidate> _candidates = [];
    private AttributionCandidate? _selectedCandidate;
    private string _state = "待处理";

    public required SubtitleSource Source
    {
        get => _source;
        set
        {
            if (Equals(_source, value)) return;
            _source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public required IReadOnlyList<AttributionCandidate> Candidates
    {
        get => _candidates;
        set
        {
            if (ReferenceEquals(_candidates, value)) return;
            _candidates = value;
            OnPropertyChanged();
        }
    }

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

    public string Summary
    {
        get
        {
            var sourceInfo = Source.Kind == SubtitleSourceKind.Archive && !Source.IsIndexed
                ? "压缩包 · 未读取"
                : $"{Source.SubtitleCount} 字幕";

            return $"{Source.DisplayName}\n{sourceInfo} · " +
                   (SelectedCandidate is null ? "需要选择目标" : $"→ {SelectedCandidate.Target.RelativePath}") +
                   $" · {State}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
