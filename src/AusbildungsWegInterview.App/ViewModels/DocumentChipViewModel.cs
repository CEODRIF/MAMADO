using System.ComponentModel;
using AusbildungsWegInterview.Localization;

namespace AusbildungsWegInterview.App.ViewModels;

/// <summary>
/// One file chip of the Document Context panel (Resume.pdf · Added · 2 pages).
/// Shows only REAL metadata: the file name the user picked, the page count when
/// the format exposes one (PDF), otherwise the imported character count.
/// </summary>
public sealed class DocumentChipViewModel : INotifyPropertyChanged
{
    private readonly Func<string, bool> _remove;

    private string _fileName = string.Empty;
    private int? _pageCount;
    private int? _charCount;
    private bool _isVisible;
    private string _meta = string.Empty;

    public DocumentChipViewModel(string slotKey, Func<string, bool> remove)
    {
        SlotKey = slotKey;
        _remove = remove;
        RemoveCommand = new RelayCommand(_ => _remove(SlotKey), _ => _isVisible);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"cv" | "jd" | "notes" — identifies the slot for the remove action.</summary>
    public string SlotKey { get; }

    public string FileName => _fileName;
    public string Meta => _meta;
    public bool IsVisible => _isVisible;

    public RelayCommand RemoveCommand { get; }

    /// <summary>Records a successful import (file name + real metadata).</summary>
    public void SetImported(string fileName, int? pageCount, int? charCount)
    {
        _fileName = fileName;
        _pageCount = pageCount;
        _charCount = charCount;
        _isVisible = true;
        Raise(nameof(FileName));
        Raise(nameof(IsVisible));
        RemoveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Clears the chip (personal data removed from the session).</summary>
    public void Clear()
    {
        _fileName = string.Empty;
        _pageCount = null;
        _charCount = null;
        _isVisible = false;
        _meta = string.Empty;
        Raise(nameof(FileName));
        Raise(nameof(Meta));
        Raise(nameof(IsVisible));
        RemoveCommand.RaiseCanExecuteChanged();
    }

    public void ApplyLocalization(LocalizationManager loc)
    {
        _meta = !_isVisible
            ? string.Empty
            : _pageCount is { } pages && pages > 0
                ? loc.F("DocChipMetaPages", loc["DocChipAdded"], pages)
                : _charCount is { } chars && chars > 0
                    ? loc.F("DocChipMetaChars", loc["DocChipAdded"], chars)
                    : loc["DocChipAdded"];
        Raise(nameof(Meta));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
