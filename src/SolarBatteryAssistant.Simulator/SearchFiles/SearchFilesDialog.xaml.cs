using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SolarBatteryAssistant.Simulator.SearchFiles;

/// <summary>
/// Dialog that lets the user search through application data files (plan and
/// rate-set JSON files) and drag selected files to other applications.
///
/// <para>
/// Drag-and-drop is initiated when the user presses the left mouse button and
/// moves beyond the system-defined minimum drag distance.  A
/// <see cref="DataObject"/> populated with <see cref="DataFormats.FileDrop"/>
/// is passed to <see cref="DragDrop.DoDragDrop"/> so that the target
/// application receives a standard file-drop payload.
/// </para>
/// </summary>
public partial class SearchFilesDialog : Window
{
    private readonly SearchFilesDialogViewModel _viewModel = new();

    // Position recorded when the left mouse button went down — used to
    // determine whether the user has moved far enough to start a drag.
    private Point _dragStartPoint;
    private bool _pendingDrag;

    public SearchFilesDialog()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchFilesDialogViewModel.FilteredFiles))
            UpdateCountLabel();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _viewModel.LoadFiles();
        UpdateCountLabel();
    }

    // -----------------------------------------------------------------------
    // Toolbar
    // -----------------------------------------------------------------------

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadFiles();
        UpdateCountLabel();
    }

    // -----------------------------------------------------------------------
    // Selection feedback
    // -----------------------------------------------------------------------

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = FileList.SelectedItems.Count;
        StatusLabel.Text = count switch
        {
            0 => "Select files and drag them to another application.",
            1 => "1 file selected — drag to copy it to another application.",
            _ => $"{count} files selected — drag to copy them to another application."
        };
    }

    // -----------------------------------------------------------------------
    // Drag-and-drop: copy selected files to other applications
    // -----------------------------------------------------------------------

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _pendingDrag = true;
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pendingDrag || e.LeftButton != MouseButtonState.Pressed)
        {
            _pendingDrag = false;
            return;
        }

        var current = e.GetPosition(null);
        var delta = _dragStartPoint - current;

        // Only start dragging once the mouse has moved beyond the system threshold
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pendingDrag = false;

        var selectedPaths = FileList.SelectedItems
            .OfType<FileItemViewModel>()
            .Select(f => f.FilePath)
            .Where(File.Exists)
            .ToArray();

        if (selectedPaths.Length == 0)
            return;

        var dataObject = new DataObject(DataFormats.FileDrop, selectedPaths);
        DragDrop.DoDragDrop(FileList, dataObject, DragDropEffects.Copy);
    }

    // -----------------------------------------------------------------------
    // Close
    // -----------------------------------------------------------------------

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void UpdateCountLabel()
        => CountLabel.Text = $"{_viewModel.FilteredFiles.Count} file(s) found";
}
