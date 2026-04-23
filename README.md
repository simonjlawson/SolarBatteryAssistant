# Solar Battery Assistant

A HomeAssistant integration and C# daemon that optimises battery usage for solar + battery systems on the **Octopus Agile** time-of-use tariff.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     SolarBatteryAssistant                    │
│                                                             │
│  ┌─────────────────────┐    ┌────────────────────────────┐  │
│  │  C# Daemon           │    │  Windows Simulator App     │  │
│  │  (planning engine)   │    │  (debug & visualise plans) │  │
│  │                      │    │                            │  │
│  │ - Octopus Agile API  │    │ - Price charts             │  │
│  │ - HA REST API        │    │ - Battery SoC chart        │  │
│  │ - 30min plan cycle   │    │ - Day simulation log       │  │
│  │ - Plan persistence   │    │ - Historical plan review   │  │
│  └─────────┬────────────┘    └───────────────┬────────────┘  │
│            │ REST API                        │               │
│            ▼                                 │               │
│  ┌─────────────────────────────────────────┐ │               │
│  │         HomeAssistant                    │◄┘               │
│  │                                         │                 │
│  │  Scenes:                                │                 │
│  │  - ImportFromGrid                       │                 │
│  │  - ExportToGrid                         │                 │
│  │  - BypassBatteryOnlyUseGrid             │                 │
│  │  - NormalBatteryMinimiseGrid            │                 │
│  │                                         │                 │
│  │  Sensors:                               │                 │
│  │  - Battery SoC (%)                      │                 │
│  │  - Solar Forecast (Wh)                  │                 │
│  └─────────────────────────────────────────┘                 │
└─────────────────────────────────────────────────────────────┘
```

## Projects

| Project | Description |
|---------|-------------|
| `SolarBatteryAssistant.Core` | Shared models, interfaces, planning engine, JSON plan storage |
| `SolarBatteryAssistant.Infrastructure` | HomeAssistant REST client, Octopus Agile price provider, solar/battery providers |
| `SolarBatteryAssistant.Daemon` | .NET 8 Worker Service — runs the planning loop |
| `SolarBatteryAssistant.Simulator` | WPF Windows app — visualise and simulate plans |
| `homeassistant/custom_components/solar_battery_assistant` | HACS-compatible HA integration scaffold |

## Setup

### 1. HomeAssistant Configuration

Copy `homeassistant/custom_components/solar_battery_assistant/` to your HA `custom_components` directory.

Configure the scenes in `configuration_example.yaml` for your specific inverter integration.

Create a **Long-Lived Access Token** in HA: *Profile → Long-Lived Access Tokens → Create Token*

### 2. Daemon Configuration

Edit `src/SolarBatteryAssistant.Daemon/appsettings.json`:

```json
{
  "SolarBatteryAssistant": {
    "HomeAssistant": {
      "BaseUrl": "http://homeassistant.local:8123",
      "AccessToken": "YOUR_LONG_LIVED_ACCESS_TOKEN",
      "SceneEntityIds": {
        "ImportFromGrid": "scene.import_from_grid",
        "ExportToGrid": "scene.export_to_grid",
        "BypassBatteryOnlyUseGrid": "scene.bypass_battery_only_use_grid",
        "NormalBatteryMinimiseGrid": "scene.normal_battery_minimise_grid"
      }
    },
    "Battery": {
      "SocEntityId": "sensor.battery_state_of_charge",
      "CapacityWh": 10000,
      "MaxImportWatts": 3000,
      "MaxExportWatts": 3000
    },
    "Solar": {
      "ForecastEntityId": "sensor.solar_forecast_today",
      "ScaleFactor": 0.7
    },
    "EnergyPricing": {
      "Octopus": {
        "RegionCode": "C",
        "ImportProductCode": "AGILE-FLEX-22-11-25"
      }
    }
  }
}
```

### 3. Run the Daemon

```bash
cd src/SolarBatteryAssistant.Daemon
dotnet run
```

Or publish as a systemd service or Windows Service.

### 4. Simulator

Open `SolarBatteryAssistant.slnx` in Visual Studio, set `SolarBatteryAssistant.Simulator` as startup project, and run.

Enter your HomeAssistant URL and token, click **Connect**, then **Load Plan** to see today's optimised schedule.

## Planning Algorithm

The planner uses a greedy strategy across the 48 half-hourly slots of the day:

1. **Ranks** all slots by import price (cheapest 25%) and export price (most expensive 25%)
2. **For each slot** (in time order, simulating battery SoC forward):
   - `ImportFromGrid` — cheap price + battery not full + grid charging enabled
   - `ExportToGrid` — expensive export price + battery has charge + export enabled  
   - `BypassBatteryOnlyUseGrid` — battery full + solar exceeding load
   - `NormalBatteryMinimiseGrid` — all other times (use solar/battery to minimise grid)
3. **Re-evaluates** every 30 minutes using the actual battery state

## Configuration Reference

| Section | Key | Description | Default |
|---------|-----|-------------|---------|
| Battery | `CapacityWh` | Total battery capacity in Wh | `10000` |
| Battery | `MaxImportWatts` | Max grid→battery charge rate | `3000` |
| Battery | `MaxExportWatts` | Max battery→grid discharge rate | `3000` |
| Battery | `MinChargePercent` | Minimum SoC to protect battery | `10` |
| Battery | `RoundTripEfficiency` | Charge/discharge efficiency | `0.90` |
| Solar | `ScaleFactor` | Multiplier on raw solar forecast | `0.7` |
| Solar | `SolarNoon` | Time of solar noon (local) | `13:00` |
| Octopus | `RegionCode` | Agile region (A-P) | `C` (London) |
| Octopus | `ImportProductCode` | Agile product code | `AGILE-FLEX-22-11-25` |
| Planning | `AllowExport` | Enable battery→grid export | `true` |
| Planning | `AllowGridCharging` | Enable grid→battery charging | `true` |
| Planning | `EodTargetChargePercent` | Target SoC at midnight | `20` |
| Planning | `TimeZoneId` | IANA timezone for local time | `Europe/London` |

## Octopus Agile Region Codes

| Code | Region |
|------|--------|
| A | East England |
| B | East Midlands |
| C | London |
| D | North Wales & Mersey |
| E | West Midlands |
| F | North East |
| G | North West |
| H | Southern |
| J | South East |
| K | South Wales |
| L | South West |
| M | Yorkshire |
| N | South Scotland |
| P | North Scotland |
