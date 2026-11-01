using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SporeModManager.Core.Models;

public class ModApiAsset
{
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class ModApiRelease : INotifyPropertyChanged
{
    private bool _isDownloaded;
    private bool _isInstalled;

    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public List<ModApiAsset> Assets { get; set; } = new();

    [JsonIgnore]
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            if (_isDownloaded == value) return;
            _isDownloaded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LocalStatus));
            OnPropertyChanged(nameof(HasLocalCopy));
        }
    }

    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LocalStatus));
            OnPropertyChanged(nameof(HasLocalCopy));
        }
    }

    [JsonIgnore]
    public string LocalStatus => IsInstalled ? "Installed" : IsDownloaded ? "Downloaded" : "";

    [JsonIgnore]
    public bool HasLocalCopy => IsInstalled || IsDownloaded;

    [JsonIgnore]
    public Version? ParsedVersion
    {
        get
        {
            var t = TagName.TrimStart('v', 'V');
            return Version.TryParse(t, out var v) ? v : null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}