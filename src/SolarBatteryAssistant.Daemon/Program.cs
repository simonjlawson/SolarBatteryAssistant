using SolarBatteryAssistant.Core;
using SolarBatteryAssistant.Daemon;
using SolarBatteryAssistant.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Core services (planner, repository, configuration binding)
builder.Services.AddSolarBatteryAssistantCore(builder.Configuration);

// Infrastructure (HomeAssistant client, Octopus prices, solar/battery providers)
builder.Services.AddSolarBatteryInfrastructure();

// Main planning worker
builder.Services.AddHostedService<PlanningWorker>();

var host = builder.Build();
host.Run();
