using Microsoft.Extensions.Options;
using SolarBatteryAssistant.Core.Configuration;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;

namespace SolarBatteryAssistant.Daemon;

/// <summary>
/// Background worker that drives the solar battery management loop.
///
/// Responsibilities:
///   1. On startup and at the start of each day, generate a new energy plan.
///   2. Every 30 minutes, re-evaluate the plan against the actual battery state.
///   3. Activate the appropriate HomeAssistant scene for the current slot.
///   4. Persist plans for later review.
/// </summary>
public class PlanningWorker : BackgroundService
{
    private readonly IEnergyPriceProvider _priceProvider;
    private readonly ISolarForecastProvider _solarProvider;
    private readonly IBatteryStateProvider _batteryProvider;
    private readonly ISceneController _sceneController;
    private readonly IEnergyPlanner _planner;
    private readonly IPlanRepository _planRepository;
    private readonly PlanningConfiguration _planConfig;
    private readonly ILogger<PlanningWorker> _logger;

    private EnergyPlan? _currentPlan;

    public PlanningWorker(
        IEnergyPriceProvider priceProvider,
        ISolarForecastProvider solarProvider,
        IBatteryStateProvider batteryProvider,
        ISceneController sceneController,
        IEnergyPlanner planner,
        IPlanRepository planRepository,
        IOptions<DaemonConfiguration> config,
        ILogger<PlanningWorker> logger)
    {
        _priceProvider = priceProvider;
        _solarProvider = solarProvider;
        _batteryProvider = batteryProvider;
        _sceneController = sceneController;
        _planner = planner;
        _planRepository = planRepository;
        _planConfig = config.Value.Planning;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SolarBatteryAssistant daemon starting.");

        // Try to load today's plan from storage (resume after restart)
        var today = DateOnly.FromDateTime(DateTime.Today);
        _currentPlan = await _planRepository.GetPlanAsync(today, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPlanningCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in planning cycle. Will retry next interval.");
            }

            // Wait until the next 30-minute boundary (+ a few seconds buffer)
            var delay = TimeUntilNextHalfHour();
            _logger.LogDebug("Next planning cycle in {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("SolarBatteryAssistant daemon stopping.");
    }

    private async Task RunPlanningCycleAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);

        // Generate or refresh the daily plan
        if (_currentPlan == null || _currentPlan.PlanDate != today)
        {
            _logger.LogInformation("Generating new daily plan for {Date}", today);
            _currentPlan = await GenerateDailyPlanAsync(today, ct);
        }
        else
        {
            // Re-evaluate existing plan with current battery state
            var batteryState = await _batteryProvider.GetCurrentStateAsync(ct);
            var currentSlot = _currentPlan.GetCurrentSlot(now);

            if (currentSlot != null)
            {
                double deviation = Math.Abs(batteryState.ChargePercent - currentSlot.BatteryChargePercentStart);
                if (deviation >= _planConfig.BatteryDeviationThresholdPercent)
                {
                    _logger.LogInformation(
                        "Battery deviation {Dev:F1}% exceeds threshold. Re-evaluating plan.", deviation);
                }
            }

            _currentPlan = await _planner.ReEvaluatePlanAsync(_currentPlan, batteryState, ct);
            await _planRepository.SavePlanAsync(_currentPlan, ct);
        }

        // Activate the scene for the current slot
        await ActivateCurrentSlotSceneAsync(now, ct);
    }

    private async Task<EnergyPlan> GenerateDailyPlanAsync(DateOnly date, CancellationToken ct)
    {
        var prices = await _priceProvider.GetPricesForDateAsync(date, ct);
        var solarForecast = await _solarProvider.GetForecastAsync(date, ct)
            ?? new Core.Models.SolarForecast { ForecastDate = date, RawForecastWatts = 0, ScaleFactor = 0.7 };
        var batteryState = await _batteryProvider.GetCurrentStateAsync(ct);

        var plan = await _planner.GeneratePlanAsync(date, prices, solarForecast, batteryState, ct);
        await _planRepository.SavePlanAsync(plan, ct);

        return plan;
    }

    private async Task ActivateCurrentSlotSceneAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_currentPlan == null) return;

        var currentSlot = _currentPlan.GetCurrentSlot(now);
        if (currentSlot == null)
        {
            _logger.LogWarning("No plan slot found for {Time}. Defaulting to NormalBatteryMinimiseGrid.", now);
            await _sceneController.ActivateSceneAsync(BatteryAction.NormalBatteryMinimiseGrid, ct);
            return;
        }

        _logger.LogInformation(
            "Slot {SlotStart:HH:mm} UTC: action={Action}, battery={BatStart:F0}%->{BatEnd:F0}%, cost={Cost:F2}p",
            currentSlot.SlotStart, currentSlot.Action,
            currentSlot.BatteryChargePercentStart, currentSlot.BatteryChargePercentEnd,
            currentSlot.EstimatedCostPence);

        await _sceneController.ActivateSceneAsync(currentSlot.Action, ct);
    }

    private static TimeSpan TimeUntilNextHalfHour()
    {
        var now = DateTime.UtcNow;
        var next = now.Minute < 30
            ? new DateTime(now.Year, now.Month, now.Day, now.Hour, 30, 5, DateTimeKind.Utc)
            : new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 5, DateTimeKind.Utc).AddHours(1);

        var delay = next - now;
        return delay < TimeSpan.Zero ? TimeSpan.FromSeconds(5) : delay;
    }
}
