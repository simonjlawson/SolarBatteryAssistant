using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveCharts;
using LiveCharts.Wpf;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.Rates;

// ---------------------------------------------------------------------------
// Per-row view model (one row = one 30-minute slot)
// ---------------------------------------------------------------------------

/// <summary>
/// Editable row in the rates DataGrid representing a single half-hourly slot.
/// String-typed price properties are used for robust two-way binding.
/// </summary>
public class RatePriceRowViewModel : INotifyPropertyChanged
{
    private string _importText = string.Empty;
    private string _exportText = string.Empty;

    public int SlotIndex { get; init; }
    public string TimeLabel { get; init; } = string.Empty;
    public DateTimeOffset SlotStart { get; init; }
    public DateTimeOffset SlotEnd { get; init; }

    /// <summary>Import price in p/kWh as an editable string.</summary>
    public string ImportPenceText
    {
        get => _importText;
        set { _importText = value; OnPropertyChanged(); }
    }

    /// <summary>Export price in p/kWh as an editable string (blank = no export tariff).</summary>
    public string ExportPenceText
    {
        get => _exportText;
        set { _exportText = value; OnPropertyChanged(); }
    }

    /// <summary>Parses the current import text to a decimal, or returns 0 on failure.</summary>
    public decimal ImportPence =>
        decimal.TryParse(ImportPenceText, out var v) ? v : 0m;

    /// <summary>Parses the current export text to a nullable decimal.</summary>
    public decimal? ExportPence =>
        string.IsNullOrWhiteSpace(ExportPenceText) ? null :
        decimal.TryParse(ExportPenceText, out var v) ? v : null;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ---------------------------------------------------------------------------
// Dialog view model
// ---------------------------------------------------------------------------

/// <summary>
/// Drives the <see cref="RatesDialog"/> window.
/// </summary>
public class RatesDialogViewModel : INotifyPropertyChanged
{
    private string _statusText = "Load or fetch rates to get started.";
    private SeriesCollection? _priceSeries;
    private List<string> _slotLabels = [];
    private ObservableCollection<RatePriceRowViewModel> _rows = [];

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public SeriesCollection? PriceSeries
    {
        get => _priceSeries;
        set { _priceSeries = value; OnPropertyChanged(); }
    }

    public List<string> SlotLabels
    {
        get => _slotLabels;
        set { _slotLabels = value; OnPropertyChanged(); }
    }

    public ObservableCollection<RatePriceRowViewModel> Rows
    {
        get => _rows;
        set { _rows = value; OnPropertyChanged(); }
    }

    /// <summary>Populates rows and refreshes the chart from an <see cref="EnergyPrice"/> list.</summary>
    public void LoadFromPrices(IReadOnlyList<EnergyPrice> prices)
    {
        var rows = new ObservableCollection<RatePriceRowViewModel>();
        for (int i = 0; i < prices.Count; i++)
        {
            var p = prices[i];
            rows.Add(new RatePriceRowViewModel
            {
                SlotIndex = i,
                TimeLabel = p.SlotStart.ToLocalTime().ToString("HH:mm"),
                SlotStart = p.SlotStart,
                SlotEnd = p.SlotEnd,
                ImportPenceText = p.ImportPencePerKwh.ToString("F2"),
                ExportPenceText = p.ExportPencePerKwh?.ToString("F2") ?? string.Empty
            });
        }

        Rows = rows;
        RefreshChart();
    }

    /// <summary>Converts the current row data back to an <see cref="EnergyPrice"/> list.</summary>
    public IReadOnlyList<EnergyPrice> ToEnergyPrices()
        => Rows.Select(r => new EnergyPrice
        {
            SlotStart = r.SlotStart,
            SlotEnd = r.SlotEnd,
            ImportPencePerKwh = r.ImportPence,
            ExportPencePerKwh = r.ExportPence
        }).ToList();

    /// <summary>Rebuilds the price chart from the current row data.</summary>
    public void RefreshChart()
    {
        if (!Rows.Any()) return;

        SlotLabels = Rows.Select(r => r.TimeLabel).ToList();

        var importValues = new ChartValues<decimal>(Rows.Select(r => r.ImportPence));

        var series = new SeriesCollection
        {
            new ColumnSeries
            {
                Title = "Import (p/kWh)",
                Values = importValues,
                Fill = System.Windows.Media.Brushes.SteelBlue,
                StrokeThickness = 0
            }
        };

        if (Rows.Any(r => !string.IsNullOrWhiteSpace(r.ExportPenceText)))
        {
            var exportValues = new ChartValues<decimal>(
                Rows.Select(r => r.ExportPence ?? 0m));
            series.Add(new ColumnSeries
            {
                Title = "Export (p/kWh)",
                Values = exportValues,
                Fill = System.Windows.Media.Brushes.DarkGreen,
                StrokeThickness = 0
            });
        }

        PriceSeries = series;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
