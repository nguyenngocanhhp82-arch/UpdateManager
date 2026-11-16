using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinUpdateManager.Core.Models;

public class WingetPackage : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _id = string.Empty;
    private string _currentVersion = string.Empty;
    private string _availableVersion = string.Empty;
    private string _source = string.Empty;
    private bool _isPinned;
    private bool _isSelected;
    private bool _isPriority;
    private bool _isExcluded;
    private string _warningNote = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        set { if (_currentVersion != value) { _currentVersion = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUpdate)); } }
    }

    public string AvailableVersion
    {
        get => _availableVersion;
        set { if (_availableVersion != value) { _availableVersion = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUpdate)); } }
    }

    public string Source
    {
        get => _source;
        set { if (_source != value) { _source = value; OnPropertyChanged(); } }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public bool IsPriority
    {
        get => _isPriority;
        set { if (_isPriority != value) { _isPriority = value; OnPropertyChanged(); } }
    }

    public bool IsExcluded
    {
        get => _isExcluded;
        set { if (_isExcluded != value) { _isExcluded = value; OnPropertyChanged(); } }
    }

    public string WarningNote
    {
        get => _warningNote;
        set { if (_warningNote != value) { _warningNote = value; OnPropertyChanged(); } }
    }

    public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion) &&
                             !string.Equals(CurrentVersion, AvailableVersion, StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{Name} [{Id}] {CurrentVersion} -> {AvailableVersion} ({Source})";
}
