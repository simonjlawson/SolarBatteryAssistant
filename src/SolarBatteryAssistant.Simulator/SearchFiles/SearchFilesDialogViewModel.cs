using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace SolarBatteryAssistant.Simulator.SearchFiles;

/// <summary>
/// Represents a single file entry shown in the <see cref="SearchFilesDialog"/> list.
/// </summary>
public class FileItemViewModel
{
    /// <summary>Full path to the file on disk.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>File name including extension.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Human-readable category label (e.g. "Plan" or "Rate Set").</summary>
    public string FileType { get; init; } = string.Empty;

    /// <summary>Formatted file size string (e.g. "4.2 KB").</summary>
    public string FileSize { get; init; } = string.Empty;

    /// <summary>Formatted last-write time (e.g. "2024-06-01 14:30").</summary>
    public string LastModified { get; init; } = string.Empty;
}

/// <summary>
/// View model for the <see cref="SearchFilesDialog"/>.
/// Discovers files from the application's data directories and provides
/// live text-based filtering.
/// </summary>
public class SearchFilesDialogViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private ObservableCollection<FileItemViewModel> _filteredFiles = [];
    private List<FileItemViewModel> _allFiles = [];

    /// <summary>
    /// Text typed into the search box; updating it immediately filters
    /// <see cref="FilteredFiles"/>.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>Files currently visible in the list after applying <see cref="SearchText"/>.</summary>
    public ObservableCollection<FileItemViewModel> FilteredFiles
    {
        get => _filteredFiles;
        private set { _filteredFiles = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Scans all known application data directories and repopulates the file list.
    /// </summary>
    public void LoadFiles()
    {
        _allFiles = [];

        // Rate-set files saved by RatesRepository
        var ratesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolarBatteryAssistant", "Rates");
        AppendFilesFromDirectory(ratesDir, "Rate Set", "*.json");

        // Plan files produced by the daemon / live-HA mode (default storage location)
        var plansDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "data", "plans"));
        AppendFilesFromDirectory(plansDir, "Plan", "*.json");

        ApplyFilter();
    }

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private void AppendFilesFromDirectory(string directory, string fileType, string pattern)
    {
        if (!Directory.Exists(directory))
            return;

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern))
            {
                var info = new FileInfo(path);
                _allFiles.Add(new FileItemViewModel
                {
                    FilePath = path,
                    FileName = info.Name,
                    FileType = fileType,
                    FileSize = FormatFileSize(info.Length),
                    LastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }
        }
        catch (IOException)
        {
            // Skip directories that cannot be read
        }
    }

    private void ApplyFilter()
    {
        var term = _searchText.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? _allFiles
            : _allFiles.Where(f =>
                f.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                f.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase));

        FilteredFiles = new ObservableCollection<FileItemViewModel>(filtered);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
