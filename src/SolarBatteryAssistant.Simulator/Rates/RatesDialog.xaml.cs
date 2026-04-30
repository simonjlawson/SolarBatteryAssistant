using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SolarBatteryAssistant.Core.Interfaces;

namespace SolarBatteryAssistant.Simulator.Rates;

/// <summary>
/// Dialog that lets the user:
/// <list type="bullet">
///   <item>Fetch today's electricity rates from the currently connected price provider.</item>
///   <item>Load previously saved rate sets from a dropdown or via a file-picker.</item>
///   <item>Visualise rates in a bar chart and edit them in a DataGrid.</item>
///   <item>Save the current rates as a named file for later use.</item>
///   <item>Confirm the rates for use in the simulator by clicking "Use These Rates".</item>
/// </list>
/// </summary>
public partial class RatesDialog : Window
{
    private readonly IEnergyPriceProvider? _priceProvider;
    private readonly RatesDialogViewModel _viewModel = new();

    /// <summary>
    /// The rates the user confirmed, or <c>null</c> if the dialog was cancelled.
    /// </summary>
    public Core.Models.EnergyPrice[]? ResultRates { get; private set; }

    /// <param name="priceProvider">
    /// Optional price provider from the currently active mode (Live HA / Demo).
    /// When supplied the "Get Today's Rates" button is enabled.
    /// </param>
    public RatesDialog(IEnergyPriceProvider? priceProvider = null)
    {
        InitializeComponent();
        DataContext = _viewModel;
        _priceProvider = priceProvider;

        GetTodaysRatesButton.IsEnabled = _priceProvider != null;

        // Keep the chart in sync with the view model
        PriceChart.Series = _viewModel.PriceSeries;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RatesDialogViewModel.PriceSeries))
                PriceChart.Series = _viewModel.PriceSeries;
        };

        RefreshSavedRatesDropdown();
    }

    // -------------------------------------------------------------------------
    // Toolbar actions
    // -------------------------------------------------------------------------

    private async void GetTodaysRates_Click(object sender, RoutedEventArgs e)
    {
        if (_priceProvider == null) return;

        SetStatus("Fetching today's rates…");
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var prices = await _priceProvider.GetPricesForDateAsync(today);
            _viewModel.LoadFromPrices(prices);
            RateSetNameBox.Text = today.ToString("yyyy-MM-dd");
            SetStatus($"Loaded {prices.Count} slots for {today:dd/MM/yyyy}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show($"Failed to fetch rates:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadSaved_Click(object sender, RoutedEventArgs e)
    {
        if (SavedRatesCombo.SelectedItem is not string name) return;

        SetStatus($"Loading '{name}'…");
        var rateSet = await RatesRepository.LoadRateSetByNameAsync(name);
        if (rateSet == null)
        {
            SetStatus("Failed to load rate set.");
            return;
        }

        _viewModel.LoadFromPrices(rateSet.Prices);
        RateSetNameBox.Text = name;
        SetStatus($"Loaded '{name}' — {rateSet.Prices.Count} slots ({rateSet.Date:dd/MM/yyyy}).");
    }

    private async void LoadFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load Rate Set",
            Filter = "Rate Set JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog(this) != true) return;

        SetStatus($"Loading from {Path.GetFileName(dlg.FileName)}…");
        var rateSet = await RatesRepository.LoadRateSetFromFileAsync(dlg.FileName);

        if (rateSet == null)
        {
            SetStatus("Failed to load file.");
            MessageBox.Show(
                "Could not read a rate set from the selected file.\n" +
                "Make sure it is a valid rate-set JSON saved by this application.",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _viewModel.LoadFromPrices(rateSet.Prices);
        RateSetNameBox.Text = !string.IsNullOrWhiteSpace(rateSet.Name)
            ? rateSet.Name
            : Path.GetFileNameWithoutExtension(dlg.FileName);
        SetStatus($"Loaded {rateSet.Prices.Count} slots from file.");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Rows.Any())
        {
            MessageBox.Show("There are no rates to save.\nLoad or fetch rates first.",
                "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = RateSetNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Enter a name for the rate set before saving.",
                "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Sanitise the name so it is safe as a file name
        var invalid = Path.GetInvalidFileNameChars();
        var sanitised = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (sanitised != name)
        {
            SetStatus($"Note: name adjusted to '{sanitised}' (invalid characters replaced).");
            name = sanitised;
        }

        try
        {
            await RatesRepository.SaveRateSetAsync(name, _viewModel.ToEnergyPrices());
            RefreshSavedRatesDropdown();
            SavedRatesCombo.SelectedItem = name;
            SetStatus($"Saved rate set '{name}'.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
            MessageBox.Show($"Failed to save rate set:\n{ex.Message}",
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -------------------------------------------------------------------------
    // DataGrid editing — refresh chart after each row is committed
    // -------------------------------------------------------------------------

    private void RatesGrid_RowEditEnding(object sender,
        System.Windows.Controls.DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != System.Windows.Controls.DataGridEditAction.Commit) return;

        // Defer so the binding has time to push the value to the view model
        Dispatcher.InvokeAsync(_viewModel.RefreshChart, DispatcherPriority.Background);
    }

    // -------------------------------------------------------------------------
    // Confirm / cancel
    // -------------------------------------------------------------------------

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Rows.Any())
        {
            MessageBox.Show(
                "No rates are loaded.\nLoad or fetch rates first, or click Cancel.",
                "No Rates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultRates = _viewModel.ToEnergyPrices().ToArray();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetStatus(string text) => StatusLabel.Text = text;

    private void RefreshSavedRatesDropdown()
    {
        var names = RatesRepository.GetSavedRateSetNames();
        SavedRatesCombo.ItemsSource = names;
    }
}
