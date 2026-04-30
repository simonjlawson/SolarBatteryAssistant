using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveCharts;
using LiveCharts.Wpf;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Simulator.ViewModels;

/// <summary>
/// View model for a plan slot row displayed in grids.
/// </summary>
public class PlanSlotViewModel : INotifyPropertyChanged
{
    private readonly PlanSlot _slot;

    public PlanSlotViewModel(PlanSlot slot)
    {
        _slot = slot;
    }

    public DateTimeOffset SlotStart => _slot.SlotStart;
    public BatteryAction Action => _slot.Action;
    public string ActionShort => _slot.Action switch
    {
        BatteryAction.ImportFromGrid => "Import",
        BatteryAction.ExportToGrid => "Export",
        BatteryAction.BypassBatteryOnlyUseGrid => "Bypass",
        BatteryAction.NormalBatteryMinimiseGrid => "Normal",
        _ => _slot.Action.ToString()
    };
    public double BatteryChargePercentStart => _slot.BatteryChargePercentStart;
    public double BatteryChargePercentEnd => _slot.BatteryChargePercentEnd;
    public decimal EstimatedCostPence => _slot.EstimatedCostPence;
    public decimal? ActualCostPence => _slot.ActualCostPence;
    public double SolarWatts => _slot.SolarWatts;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Main view model for the simulator window.
/// </summary>
public class SimulatorViewModel : INotifyPropertyChanged
{
    private EnergyPlan? _currentPlan;
    private string _slotLabels = string.Empty;
    private SeriesCollection? _priceSeries;
    private SeriesCollection? _batterySeries;

    public EnergyPlan? CurrentPlan
    {
        get => _currentPlan;
        set
        {
            _currentPlan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlanDate));
            OnPropertyChanged(nameof(GeneratedAt));
            OnPropertyChanged(nameof(BatterySoc));
            OnPropertyChanged(nameof(SolarForecast));
            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(SlotViewModels));
            RefreshCharts();
        }
    }

    public string PlanDate => _currentPlan?.PlanDate.ToString("dddd, d MMMM yyyy") ?? "-";
    public string GeneratedAt => _currentPlan?.GeneratedAt.ToLocalTime().ToString("HH:mm:ss dd/MM/yyyy") ?? "-";
    public string BatterySoc => _currentPlan != null
        ? $"{_currentPlan.InitialBatteryState.ChargePercent:F1}%"
        : "-";
    public string SolarForecast => _currentPlan != null
        ? $"{_currentPlan.SolarForecast.EffectiveWatts:F0} Wh (raw: {_currentPlan.SolarForecast.RawForecastWatts:F0} Wh × {_currentPlan.SolarForecast.ScaleFactor:P0})"
        : "-";
    public string TotalCost => _currentPlan != null
        ? $"{_currentPlan.TotalEstimatedCostPence:F2}p"
        : "-";

    public List<PlanSlotViewModel> SlotViewModels => _currentPlan?.Slots
        .Select(s => new PlanSlotViewModel(s))
        .ToList() ?? [];

    public List<string> SlotLabels { get; private set; } = [];
    public SeriesCollection? PriceSeries { get => _priceSeries; set { _priceSeries = value; OnPropertyChanged(); } }
    public SeriesCollection? BatterySeries { get => _batterySeries; set { _batterySeries = value; OnPropertyChanged(); } }

    private void RefreshCharts()
    {
        if (_currentPlan == null) return;

        var slots = _currentPlan.Slots;
        SlotLabels = slots.Select(s => s.SlotStart.ToLocalTime().ToString("HH:mm")).ToList();
        OnPropertyChanged(nameof(SlotLabels));

        // Price chart — import price bars coloured by action
        var importValues = new ChartValues<decimal>(slots.Select(s => s.Price.ImportPencePerKwh));

        PriceSeries = new SeriesCollection
        {
            new ColumnSeries
            {
                Title = "Import (p/kWh)",
                Values = importValues,
                Fill = System.Windows.Media.Brushes.SteelBlue,
                StrokeThickness = 0
            }
        };

        if (slots.Any(s => s.Price.ExportPencePerKwh.HasValue))
        {
            var exportValues = new ChartValues<decimal>(
                slots.Select(s => s.Price.ExportPencePerKwh ?? 0));
            PriceSeries.Add(new ColumnSeries
            {
                Title = "Export (p/kWh)",
                Values = exportValues,
                Fill = System.Windows.Media.Brushes.DarkGreen,
                StrokeThickness = 0
            });
        }

        // Battery SoC line chart
        var socValues = new ChartValues<double>(slots.Select(s => s.BatteryChargePercentEnd));
        BatterySeries = new SeriesCollection
        {
            new LineSeries
            {
                Title = "SoC %",
                Values = socValues,
                PointGeometry = null,
                Fill = System.Windows.Media.Brushes.Transparent,
                Stroke = System.Windows.Media.Brushes.Orange,
                StrokeThickness = 2
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
