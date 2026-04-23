using System.Text;
using System.Windows;
using System.Windows.Controls;
using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolarBatteryAssistant.Core;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Infrastructure;
using SolarBatteryAssistant.Simulator.ViewModels;

namespace SolarBatteryAssistant.Simulator;

/// <summary>
/// Main simulator window. Supports:
/// - Loading and displaying plans from storage
/// - Simulating a day's actions and estimated cost
/// - Reviewing historical plan data
/// - Connecting to live HomeAssistant to fetch real prices/battery state
/// </summary>
public partial class MainWindow : Window
{
    private readonly SimulatorViewModel _viewModel = new();
    private IHost? _serviceHost;
    private IEnergyPriceProvider? _priceProvider;
    private ISolarForecastProvider? _solarProvider;
    private IBatteryStateProvider? _batteryProvider;
    private IEnergyPlanner? _planner;
    private IPlanRepository? _planRepository;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DateSelector.SelectedDate = DateTime.Today;

        PriceChart.Series = _viewModel.PriceSeries;
        BatteryChart.Series = _viewModel.BatterySeries;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SimulatorViewModel.PriceSeries))
                PriceChart.Series = _viewModel.PriceSeries;
            if (e.PropertyName == nameof(SimulatorViewModel.BatterySeries))
                BatteryChart.Series = _viewModel.BatterySeries;
        };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        StatusLabel.Text = "Connecting...";
        FooterLabel.Text = "Establishing connection to HomeAssistant...";

        try
        {
            _serviceHost = BuildServiceHost();
            await _serviceHost.StartAsync();

            _priceProvider = _serviceHost.Services.GetRequiredService<IEnergyPriceProvider>();
            _solarProvider = _serviceHost.Services.GetRequiredService<ISolarForecastProvider>();
            _batteryProvider = _serviceHost.Services.GetRequiredService<IBatteryStateProvider>();
            _planner = _serviceHost.Services.GetRequiredService<IEnergyPlanner>();
            _planRepository = _serviceHost.Services.GetRequiredService<IPlanRepository>();

            StatusLabel.Text = "Connected";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Green;
            FooterLabel.Text = "Connected to HomeAssistant. Ready.";

            await RefreshHistoryDatesAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Connection failed";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            FooterLabel.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadPlan_Click(object sender, RoutedEventArgs e)
    {
        if (DateSelector.SelectedDate == null) return;

        var date = DateOnly.FromDateTime(DateSelector.SelectedDate.Value);

        if (_planRepository != null)
        {
            var plan = await _planRepository.GetPlanAsync(date);
            if (plan != null)
            {
                DisplayPlan(plan);
                FooterLabel.Text = $"Loaded stored plan for {date:dd/MM/yyyy}";
                return;
            }
        }

        // Generate a new plan if none stored
        if (_priceProvider != null && _solarProvider != null && _batteryProvider != null && _planner != null)
        {
            FooterLabel.Text = "Fetching prices and generating plan...";
            try
            {
                var prices = await _priceProvider.GetPricesForDateAsync(date);
                var solar = await _solarProvider.GetForecastAsync(date)
                    ?? new SolarForecast { ForecastDate = date, RawForecastWatts = 0, ScaleFactor = 0.7 };
                var battery = await _batteryProvider.GetCurrentStateAsync();
                var plan = await _planner.GeneratePlanAsync(date, prices, solar, battery);

                if (_planRepository != null)
                    await _planRepository.SavePlanAsync(plan);

                DisplayPlan(plan);
                FooterLabel.Text = $"Generated new plan for {date:dd/MM/yyyy}";
            }
            catch (Exception ex)
            {
                FooterLabel.Text = $"Error generating plan: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Please connect to HomeAssistant first.", "Not Connected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SimulateDay_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentPlan == null)
        {
            MessageBox.Show("Please load a plan first.", "No Plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== Simulation: {_viewModel.CurrentPlan.PlanDate:dd/MM/yyyy} ===");
        sb.AppendLine($"Battery start: {_viewModel.CurrentPlan.InitialBatteryState.ChargePercent:F1}%");
        sb.AppendLine($"Solar forecast: {_viewModel.CurrentPlan.SolarForecast.EffectiveWatts:F0} Wh");
        sb.AppendLine();
        sb.AppendLine($"{"Time",-6} {"Action",-25} {"SoC%",-8} {"Solar W",-10} {"Cost p",-10}");
        sb.AppendLine(new string('-', 65));

        decimal runningCost = 0;
        foreach (var slot in _viewModel.CurrentPlan.Slots)
        {
            runningCost += slot.EstimatedCostPence;
            sb.AppendLine(
                $"{slot.SlotStart.ToLocalTime():HH:mm} " +
                $"{slot.Action,-25} " +
                $"{slot.BatteryChargePercentEnd,6:F1}% " +
                $"{slot.SolarWatts,8:F0}W " +
                $"{slot.EstimatedCostPence,9:F2}p");
        }

        sb.AppendLine(new string('-', 65));
        sb.AppendLine($"{"Total estimated cost:",-40} {runningCost:F2}p");

        SimulationLog.Text = sb.ToString();
        FooterLabel.Text = $"Simulation complete. Estimated cost: {runningCost:F2}p";
        await Task.CompletedTask;
    }

    private void DateSelector_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        // Date selection is applied when LoadPlan or SimulateDay is explicitly clicked
    }

    private async void HistoryDate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_planRepository == null || HistoryDates.SelectedItem is not DateOnly date) return;

        var plan = await _planRepository.GetPlanAsync(date);
        if (plan != null)
        {
            DisplayPlan(plan);
            HistoryGrid.ItemsSource = plan.Slots
                .Select(s => new PlanSlotViewModel(s))
                .ToList();
        }
    }

    private void DisplayPlan(EnergyPlan plan)
    {
        _viewModel.CurrentPlan = plan;
        SlotGrid.ItemsSource = _viewModel.SlotViewModels;

        PlanDateLabel.Text = _viewModel.PlanDate;
        GeneratedLabel.Text = _viewModel.GeneratedAt;
        BatterySocLabel.Text = _viewModel.BatterySoc;
        SolarForecastLabel.Text = _viewModel.SolarForecast;
        TotalCostLabel.Text = _viewModel.TotalCost;
    }

    private async Task RefreshHistoryDatesAsync()
    {
        if (_planRepository == null) return;
        var dates = await _planRepository.GetAvailablePlanDatesAsync();
        HistoryDates.ItemsSource = dates;
    }

    private IHost BuildServiceHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{DaemonConfiguration.SectionName}:HomeAssistant:BaseUrl"] = HaUrlBox.Text,
                        [$"{DaemonConfiguration.SectionName}:HomeAssistant:AccessToken"] = HaTokenBox.Password,
                        [$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:RegionCode"] = "C",
                        [$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:ImportProductCode"] = "AGILE-FLEX-22-11-25"
                    })
                    .Build();

                services.AddSolarBatteryAssistantCore(config);
                services.AddSolarBatteryInfrastructure();
            })
            .Build();
    }
}