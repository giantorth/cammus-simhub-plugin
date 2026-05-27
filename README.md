# Unofficial Cammus SimHub Plugin

A SimHub plugin that bridges SimHub's LED telemetry pipeline to Cammus brand racing wheels.

> [!NOTE]
> Cammus is a brand of Beijing Cammus Industrial Control Technology Co., Ltd. This project is not affiliated with, endorsed by, or sponsored by Cammus. All trademarks are the property of their respective owners.

> [!WARNING]
> **Prototype / MVP.** Builds cleanly but has not been validated against physical hardware. The protocol details come from reverse-engineering work in the [monocoque](https://github.com/Spacefreak18/monocoque) project. Expect rough edges. Use at your own risk.

## What it does

- Registers each connected Cammus wheel as a SimHub device, so it appears in **Settings → Devices** alongside any other LED hardware
- Injects a virtual LED driver into SimHub's effects pipeline, so the wheel's RPM LEDs can be driven by SimHub's built-in effects (RPM percentage, redline curves, shift indicators, custom effects, etc.)
- Forwards velocity and gear from the active game's telemetry into the same HID report that controls the LEDs
- Single-tab settings panel with a connection pill, detected wheel model, live telemetry readout, and a "send test pattern" button

The Cammus firmware accepts a single intensity value (lit-LED count for C5, RPM percent for C12) rather than per-LED RGB, so the plugin collapses SimHub's computed `Color[]` into that count by walking left-to-right and counting non-black entries. This means SimHub's redline curve, brightness slider, and blink thresholds all naturally apply.

## Supported devices

| Model | VID    | PID    | RPM LEDs | HID report |
|-------|--------|--------|----------|------------|
| C5    | 0x3416 | 0x1021 | 9        | 14 bytes   |
| C12   | 0x3416 | 0x1023 | 10\*     | 16 bytes   |

\* The C12 firmware accepts an RPM percentage and decides for itself how to light its LEDs; the count of 10 is an assumption used only to size SimHub's effects UI. Confirm against your hardware.

## Installation

1. Download `CammusPlugin_<version>.zip` from the [Releases](../../releases) page.
2. Extract `CammusPlugin.dll` into your SimHub installation directory (SimHub defaults to `C:\Program Files (x86)\SimHub\`).
3. Restart SimHub. The plugin appears under **Settings → Plugins** as "Cammus Control". Enable it if it isn't already.
4. On first run, the plugin writes `Cammus C5\device.json` and `Cammus C12\device.json` to `<SimHub>\DevicesDefinitions\User\`.
5. Restart SimHub once more. Open **Settings → Devices → Add Device** and add the Cammus entry matching your wheel.

Requires SimHub 9.11 or newer.

If the devices don't appear in the Add Device picker, delete `<SimHub>\PluginsData\DevicesDesccriptorCache.json` and restart SimHub — that file caches device descriptors and can hold a stale entry across template changes.

## Building

### Prerequisites
- .NET SDK 8.0 or 9.0
- The repo includes vendored SimHub reference DLLs in `libs/SimHub/`. These are compile-time only; SimHub provides the runtime copies. You should not need to install SimHub to build.

### Windows

```powershell
dotnet build -c Release
```

Output: `bin\x86\Release\CammusPlugin.dll`.

To auto-deploy on every build, set the `SIMHUB_PATH` environment variable to your SimHub install directory; the `CopyToSimHub` MSBuild target will copy the built DLL there.

```powershell
$env:SIMHUB_PATH = "C:\Program Files (x86)\SimHub"
dotnet build -c Release
```

### Linux

Same command:

```bash
dotnet build -c Release
```

Output: `bin/x86/Release/CammusPlugin.dll`.

Linux builds target `net48` against the bundled reference assemblies — no Windows or SimHub install required. To copy to a SimHub install (e.g. one running under Proton/Wine), set `SIMHUB_PATH`:

```bash
export SIMHUB_PATH="$HOME/.steam/steam/steamapps/compatdata/<id>/pfx/drive_c/Program Files (x86)/SimHub"
dotnet build -c Release
```

## How it works

| Component | File | Role |
|-----------|------|------|
| Plugin entry point | `CammusPlugin.cs` | Singleton, `IPlugin`/`IDataPlugin`/`IWPFSettingsV2`. Deploys both `device.json` templates eagerly on Init, opens HID, caches telemetry per frame |
| HID transport | `Devices/CammusHidConnection.cs` | `HidSharp` wrapper. Probes by VID 0x3416 + PID 0x1021/0x1023, opens write stream, auto-reconnects on I/O failure |
| Report builder | `Devices/CammusWheelModel.cs` | Pure functions producing the C5 (14-byte) and C12 (16-byte) HID reports from `(lit, velocity, gear)` |
| Virtual LED driver | `Devices/CammusLedDeviceManager.cs` | Implements SimHub's `ILedDeviceManager`. Counts non-black colors in `Display()`, combines with cached velocity/gear, writes one HID report per frame |
| Device extension | `Devices/CammusWheelDeviceExtension.cs` | Injected per device instance. Defers the virtual-driver swap to the first `DataUpdate()` (required — see comment for the SimHub init-ordering gotcha) |
| Template deployer | `Devices/CammusDeviceDefinitionDeployer.cs` | Extracts the embedded `device.json` resources into `<SimHub>\DevicesDefinitions\User\` on Init |
| UI | `UI/SettingsControl.xaml` + `.cs` | Single-tab status panel |

## Acknowledgements

- **[monocoque](https://github.com/Spacefreak18/monocoque)** — reverse-engineered the Cammus C5 and C12 HID report formats. Without that work this plugin doesn't exist.
- **[moza-simhub-plugin](https://github.com/giantorth/moza-simhub-plugin)** — the architectural template, especially the virtual `ILedDeviceManager` injection pattern documented in its `docs/simhub.md`.

## License

GPL-3.0 (matching the upstream projects this depends on).
