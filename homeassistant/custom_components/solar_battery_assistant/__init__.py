"""
Solar Battery Assistant - HomeAssistant Custom Integration.

This integration provides the HomeAssistant side of the Solar Battery Assistant system.
The main planning logic runs in a separate C# daemon that connects to HomeAssistant via the REST API.

This integration exposes:
  - The four battery control scenes (ImportFromGrid, ExportToGrid, BypassBatteryOnlyUseGrid, NormalBatteryMinimiseGrid)
  - An input_number for the solar forecast entity
  - The current plan status as a sensor

See the main README for setup instructions.
"""

DOMAIN = "solar_battery_assistant"
