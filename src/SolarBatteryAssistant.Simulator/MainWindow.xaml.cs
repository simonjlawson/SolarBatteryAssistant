using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SolarBatteryAssistant.Core;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Infrastructure;
using SolarBatteryAssistant.Simulator.DaemonApi;
using SolarBatteryAssistant.Simulator.Demo;
using SolarBatteryAssistant.Simulator.Rates;
using SolarBatteryAssistant.Simulator.ViewModels;

namespace SolarBatteryAssistant.Simulator;

/// <summary>
/// Main simulator window — supports three operating modes:
///
///  1. <b>Demo Mode</b> — no external connections needed.
///     Uses built-in synthetic price/solar/battery providers so you can explore
///     the planner output immediately.
///
///  2. <b>Daemon Mode</b> — connects to a locally-running
///     SolarBatteryAssistant daemon via its REST API (<c>/api/plans</c>).
///     Lets you browse and visualise plans the daemon has already generated.
///
///  3. <b>Live HA Mode</b> — connects directly to HomeAssistant, fetches
///     real Octopus prices and battery state, and generates plans on demand.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SimulatorViewModel _viewModel = new();

    // Shared planning services (set by whichever mode is active)
    private IHost? _serviceHost;
    private IEnergyPriceProvider? _priceProvider;
    private ISolarForecastProvider? _solarProvider;
    private IBatteryStateProvider? _batteryProvider;
    private IEnergyPlanner? _planner;
    private IPlanRepository? _planRepository;

    // Daemon API client (owns its own HttpClient lifecycle)
    private DaemonApiClient? _daemonClient;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DateSelector.SelectedDate = DateTime.Today;

        // Try to autofill HomeAssistant token from user secrets (key: HATOKEN)
        try
        {
            var secretConfig = new ConfigurationBuilder()
                .AddUserSecrets<MainWindow>()
                .Build();

            var haToken = secretConfig["HATOKEN"];
            if (!string.IsNullOrWhiteSpace(haToken))
                HaTokenBox.Password = haToken;
        }
        catch
        {
            // Ignore failures loading user secrets — simulator should still work without them
        }

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

    // -----------------------------------------------------------------------
    // Demo Mode
    // -----------------------------------------------------------------------

    private async void DemoMode_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Demo", Brushes.DarkGreen);
        FooterLabel.Text = "Demo mode — using synthetic example data.";

        // Parse optional overrides from the toolbar
        double soc = double.TryParse(DemoBatteryBox.Text, out var s) ? Math.Clamp(s, 0, 100) : 50.0;
        double solarKwh = double.TryParse(DemoSolarBox.Text, out var kw) ? Math.Max(0, kw) : 20.0;
        double dailyLoadKwh = double.TryParse(DemoDailyLoadBox.Text, out var dl) ? Math.Max(0, dl) : 8.0;
        double constantLoadWatts = double.TryParse(DemoConstantLoadBox.Text, out var cl) ? Math.Max(0, cl) : 100.0;
        decimal cheapThreshold = decimal.TryParse(DemoCheapThresholdBox.Text, out var ct) ? ct : 2.0m;
        decimal? staticExport = decimal.TryParse(DemoStaticExportBox.Text, out var se) ? se : (decimal?)null;
        bool allowExport = DemoAllowExportBox.IsChecked ?? true;
        bool allowGridCharging = DemoAllowGridChargingBox.IsChecked ?? true;

        // Release any previous live connection
        ResetConnections();

        // Wire up synthetic providers
        var battery = new DemoBatteryStateProvider { ChargePercent = soc };
        var solar = new DemoSolarForecastProvider { PeakDailyWh = solarKwh * 1000 };
        var prices = new DemoEnergyPriceProvider();

        // Create planner using the Core service (needs DI for config defaults)
        var host = BuildDemoHost(battery, solar, prices, dailyLoadKwh, constantLoadWatts, cheapThreshold, staticExport, allowExport, allowGridCharging);
        await host.StartAsync();
        _serviceHost = host;

        _priceProvider = host.Services.GetRequiredService<IEnergyPriceProvider>();
        _solarProvider = host.Services.GetRequiredService<ISolarForecastProvider>();
        _batteryProvider = host.Services.GetRequiredService<IBatteryStateProvider>();
        _planner = host.Services.GetRequiredService<IEnergyPlanner>();
        _planRepository = null; // demo doesn't persist plans

        // Immediately generate a plan for the selected date
        await GenerateAndDisplayPlanAsync();
    }

    // -----------------------------------------------------------------------
    // Daemon Mode
    // -----------------------------------------------------------------------

    private async void ConnectDaemon_Click(object sender, RoutedEventArgs e)
    {
        var url = DaemonUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Enter the daemon URL (e.g. http://localhost:5100).",
                "Daemon URL required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetStatus("Connecting...", Brushes.Gray);
        FooterLabel.Text = $"Connecting to daemon at {url}...";

        try
        {
            ResetConnections();
            _daemonClient = new DaemonApiClient(url);

            if (!await _daemonClient.PingAsync())
            {
                SetStatus("Daemon unreachable", Brushes.Red);
                FooterLabel.Text = "Could not reach the daemon. Is it running?";
                MessageBox.Show($"Could not reach the daemon at {url}.\n\nMake sure the daemon is running.",
                    "Daemon not reachable", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // In daemon mode the client acts as plan repository AND battery provider
            _planRepository = _daemonClient;
            _batteryProvider = _daemonClient;
            _priceProvider = null;  // plans are pre-built by the daemon
            _solarProvider = null;
            _planner = null;

            SetStatus($"Daemon ({url})", Brushes.Green);
            FooterLabel.Text = $"Connected to daemon at {url}.";
            await RefreshHistoryDatesAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Error", Brushes.Red);
            FooterLabel.Text = $"Daemon connection error: {ex.Message}";
            MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -----------------------------------------------------------------------
    // Live HA Mode
    // -----------------------------------------------------------------------

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Connecting...", Brushes.Gray);
        FooterLabel.Text = "Establishing connection to HomeAssistant...";

        try
        {
            ResetConnections();
            _serviceHost = BuildHaServiceHost();
            await _serviceHost.StartAsync();

            _priceProvider = _serviceHost.Services.GetRequiredService<IEnergyPriceProvider>();
            _solarProvider = _serviceHost.Services.GetRequiredService<ISolarForecastProvider>();
            _batteryProvider = _serviceHost.Services.GetRequiredService<IBatteryStateProvider>();
            _planner = _serviceHost.Services.GetRequiredService<IEnergyPlanner>();
            _planRepository = _serviceHost.Services.GetRequiredService<IPlanRepository>();

            SetStatus("Connected (HA)", Brushes.Green);
            FooterLabel.Text = "Connected to HomeAssistant. Ready.";
            await RefreshHistoryDatesAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Connection failed", Brushes.Red);
            FooterLabel.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -----------------------------------------------------------------------
    // Shared plan actions
    // -----------------------------------------------------------------------

    private async void LoadPlan_Click(object sender, RoutedEventArgs e)
    {
        if (DateSelector.SelectedDate == null) return;
        var date = DateOnly.FromDateTime(DateSelector.SelectedDate.Value);

        // Try stored plan first
        if (_planRepository != null)
        {
            var stored = await _planRepository.GetPlanAsync(date);
            if (stored != null)
            {
                DisplayPlan(stored);
                FooterLabel.Text = $"Loaded stored plan for {date:dd/MM/yyyy}.";
                return;
            }
        }

        // Generate on-the-fly if providers are available
        if (_priceProvider != null && _solarProvider != null && _batteryProvider != null && _planner != null)
        {
            await GenerateAndDisplayPlanAsync(date);
        }
        else
        {
            FooterLabel.Text = "No plan available for this date. Try Demo Mode or connect to HA/daemon.";
            MessageBox.Show(
                "No stored plan found for this date.\n\n" +
                "• Use 'Demo Mode' to generate a synthetic plan.\n" +
                "• Connect to a daemon or live HA to fetch real plans.",
                "No Plan Available",
                MessageBoxButton.OK, MessageBoxImage.Information);
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

    // -----------------------------------------------------------------------
    // Rates dialog
    // -----------------------------------------------------------------------

    private void EditRates_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RatesDialog(_priceProvider) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultRates == null)
            return;

        _priceProvider = new StaticEnergyPriceProvider(dialog.ResultRates);
        FooterLabel.Text = $"Custom rates loaded ({dialog.ResultRates.Length} slots). " +
                           "Click 'Load Plan' or '▶ Demo Mode' to simulate with these rates.";
        SetStatus("Custom Rates", Brushes.DarkOrange);
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task GenerateAndDisplayPlanAsync(DateOnly? overrideDate = null)
    {
        var date = overrideDate ?? (DateSelector.SelectedDate.HasValue
            ? DateOnly.FromDateTime(DateSelector.SelectedDate.Value)
            : DateOnly.FromDateTime(DateTime.Today));

        FooterLabel.Text = "Fetching prices and generating plan...";
        try
        {
            var prices = await _priceProvider!.GetPricesForDateAsync(date);
            var solar = await _solarProvider!.GetForecastAsync(date)
                        ?? new SolarForecast { ForecastDate = date, RawForecastWatts = 0, ScaleFactor = 0.7 };
            var battery = await _batteryProvider!.GetCurrentStateAsync();
            var plan = await _planner!.GeneratePlanAsync(date, prices, solar, battery);

            if (_planRepository != null)
                await _planRepository.SavePlanAsync(plan);

            DisplayPlan(plan);
            FooterLabel.Text = $"Generated plan for {date:dd/MM/yyyy}.";
        }
        catch (Exception ex)
        {
            FooterLabel.Text = $"Error generating plan: {ex.Message}";
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void SetStatus(string text, Brush colour)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = colour;
    }

    private void ResetConnections()
    {
        _serviceHost?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _serviceHost?.Dispose();
        _serviceHost = null;

        _daemonClient?.Dispose();
        _daemonClient = null;

        _priceProvider = null;
        _solarProvider = null;
        _batteryProvider = null;
        _planner = null;
        _planRepository = null;
    }

    // -----------------------------------------------------------------------
    // DI host builders
    // -----------------------------------------------------------------------

    private IHost BuildDemoHost(
        DemoBatteryStateProvider battery,
        DemoSolarForecastProvider solar,
        DemoEnergyPriceProvider prices,
        double dailyLoadKwh = 8.0,
        double constantLoadWatts = 100.0,
        decimal cheapThresholdPence = 2.0m,
        decimal? staticExportPence = null,
        bool allowExport = true,
        bool allowGridCharging = true)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                var configValues = new Dictionary<string, string?>
                {
                    [$"{DaemonConfiguration.SectionName}:Battery:EstimatedDailyLoadWh"] =
                        (dailyLoadKwh * 1000).ToString("F0"),
                    [$"{DaemonConfiguration.SectionName}:Battery:ConstantLoadWatts"] =
                        constantLoadWatts.ToString("F0"),
                    [$"{DaemonConfiguration.SectionName}:Planning:VeryCheapImportThresholdPence"] =
                        cheapThresholdPence.ToString("F2"),
                    [$"{DaemonConfiguration.SectionName}:Planning:AllowExport"] =
                        allowExport.ToString(),
                    [$"{DaemonConfiguration.SectionName}:Planning:AllowGridCharging"] =
                        allowGridCharging.ToString(),
                };

                if (staticExportPence.HasValue)
                    configValues[$"{DaemonConfiguration.SectionName}:Planning:StaticExportPencePerKwh"] =
                        staticExportPence.Value.ToString("F2");

                var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(configValues)
                    .Build();

                services.AddSolarBatteryAssistantCore(config);

                // Override the infrastructure providers with demo implementations
                services.AddSingleton<IEnergyPriceProvider>(prices);
                services.AddSingleton<ISolarForecastProvider>(solar);
                services.AddSingleton<IBatteryStateProvider>(battery);
            })
            .Build();
    }

    private IHost BuildHaServiceHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                var configValues = new Dictionary<string, string?>
                {
                    [$"{DaemonConfiguration.SectionName}:HomeAssistant:BaseUrl"] = HaUrlBox.Text,
                    [$"{DaemonConfiguration.SectionName}:HomeAssistant:AccessToken"] = HaTokenBox.Password,
                    [$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:RegionCode"] = "C",
                    [$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:ImportProductCode"] = "AGILE-FLEX-22-11-25"
                };

                var octopusApiKey = OctopusApiKeyBox.Password;
                if (!string.IsNullOrWhiteSpace(octopusApiKey))
                    configValues[$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:ApiKey"] = octopusApiKey;

                var octopusAccount = OctopusAccountBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(octopusAccount))
                    configValues[$"{DaemonConfiguration.SectionName}:EnergyPricing:Octopus:AccountNumber"] = octopusAccount;

                var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(configValues)
                    .Build();

                services.AddSolarBatteryAssistantCore(config);
                services.AddSolarBatteryInfrastructure();
            })
            .Build();
    }
}
