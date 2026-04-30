using System.Text.Json;
using SolarBatteryAssistant.Core;
using SolarBatteryAssistant.Core.Interfaces;
using SolarBatteryAssistant.Core.Models;
using SolarBatteryAssistant.Daemon;
using SolarBatteryAssistant.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Core services (planner, repository, configuration binding)
builder.Services.AddSolarBatteryAssistantCore(builder.Configuration);

// Infrastructure (HomeAssistant client, Octopus prices, solar/battery providers)
builder.Services.AddSolarBatteryInfrastructure();

// Main planning worker
builder.Services.AddHostedService<PlanningWorker>();

// JSON serialization options
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// -----------------------------------------------------------------------
// Daemon REST API — allows the Simulator (and other tools) to read the
// current plan and historical plans without a HomeAssistant connection.
// -----------------------------------------------------------------------

var api = app.MapGroup("/api");

/// <summary>GET /api/status — daemon health and current battery action.</summary>
api.MapGet("/status", async (IBatteryStateProvider battery, ISceneController scenes, CancellationToken ct) =>
{
    var state = await battery.GetCurrentStateAsync(ct);
    var action = await scenes.GetCurrentActionAsync(ct);
    return Results.Ok(new
    {
        timestamp = DateTimeOffset.UtcNow,
        batteryChargePercent = state.ChargePercent,
        batteryTimestamp = state.Timestamp,
        currentAction = action?.ToString()
    });
});

/// <summary>GET /api/plans — list dates of stored plans (newest first).</summary>
api.MapGet("/plans", async (IPlanRepository repo, CancellationToken ct) =>
{
    var dates = await repo.GetAvailablePlanDatesAsync(ct);
    return Results.Ok(dates.Select(d => d.ToString("yyyy-MM-dd")));
});

/// <summary>GET /api/plans/today — today's plan.</summary>
api.MapGet("/plans/today", async (IPlanRepository repo, CancellationToken ct) =>
{
    var today = DateOnly.FromDateTime(DateTime.Today);
    var plan = await repo.GetPlanAsync(today, ct);
    return plan is null ? Results.NotFound() : Results.Ok(plan);
});

/// <summary>GET /api/plans/{date} — plan for a specific date (yyyy-MM-dd).</summary>
api.MapGet("/plans/{date}", async (string date, IPlanRepository repo, CancellationToken ct) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed))
        return Results.BadRequest("Date must be in yyyy-MM-dd format.");

    var plan = await repo.GetPlanAsync(parsed, ct);
    return plan is null ? Results.NotFound() : Results.Ok(plan);
});

/// <summary>GET /api/rates/{date} — unit rates for a specific date (yyyy-MM-dd).</summary>
api.MapGet("/rates/{date}", async (string date, IEnergyPriceProvider prices, CancellationToken ct) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed))
        return Results.BadRequest("Date must be in yyyy-MM-dd format.");

    var list = await prices.GetPricesForDateAsync(parsed, ct);
    return Results.Ok(list);
});

/// <summary>GET /api/rates/today — today's unit rates.</summary>
api.MapGet("/rates/today", async (IEnergyPriceProvider prices, CancellationToken ct) =>
{
    var today = DateOnly.FromDateTime(DateTime.Today);
    var list = await prices.GetPricesForDateAsync(today, ct);
    return Results.Ok(list);
});

/// <summary>DELETE /api/plans — clear all stored plan files and caches.</summary>
api.MapDelete("/plans", async (IPlanRepository repo, CancellationToken ct) =>
{
    await repo.ClearAllPlansAsync(ct);
    return Results.Ok(new { message = "Cleared stored plans." });
});

app.Run();
