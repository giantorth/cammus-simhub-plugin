# Development Notes

Internal reference for how the Cammus SimHub plugin works: the wire protocol, the
SimHub integration surface, and where every fact in here came from. Read this before
changing the HID report layout or the LED-driver injection path.

## Source references

| What | Where |
| --- | --- |
| Cammus HID protocol | monocoque commit `a81004418dda6e7f59cd560a546ff55adb670386` — <https://github.com/Spacefreak18/monocoque/commit/a81004418dda6e7f59cd560a546ff55adb670386> |
| Protocol discussion | monocoque issue #8 — <https://github.com/Spacefreak18/monocoque/issues/8> |
| SimHub LED-driver injection technique | `../moza-simhub-plugin/docs/simhub.md` |
| Reference implementation we cloned | `../moza-simhub-plugin/` (one directory up) |

The protocol details below were transcribed from the monocoque commit. They have **not**
been confirmed against physical hardware in this repo — see the open questions at the end.

## The Cammus wire protocol

Cammus wheels are **not** per-LED RGB devices. The firmware accepts a single intensity
number plus velocity and gear, and decides on its own how to drive the onboard RPM LEDs.
This is the same shape as Moza's "Old Protocol Wheel" (mono RPM LEDs, no color), which is
why the plugin collapses SimHub's computed `Color[]` down to a single lit-count instead of
forwarding colors.

Both reports are sent as raw HID output writes via HidSharp. **Byte `[0]` of the buffer is
the HID report ID (`0x00`) and is stripped by HidSharp/Windows before transmission** — it
does *not* go on the wire. The protocol payload (the `0xFC` / `0xFA` header) therefore starts
at buffer byte `[1]`. The buffer is one byte longer than the on-wire report to carry this
prefix (C5: 15-byte buffer → 14 wire bytes; C12: 17-byte buffer → 16 wire bytes).

> A previous version put `0xFC` at byte `[0]`, where it was consumed as a (bogus) report ID
> and never reached the wheel — captures showed the wire payload starting at the lit byte with
> no `0xFC` header. Keep the report ID at `[0]` and the header at `[1]`.

The byte offsets below are **buffer** offsets (`BuildC5` / `BuildC12` write these); subtract 1
for the on-wire offset.

### C5 — VID `0x3416` / PID `0x1021`, 14-byte wire report (15-byte buffer)

```
byte  0    = 0x00                 HID report ID (stripped before the wire)
byte  1    = 0xFC                 header
byte  2    = lit-count 0..9       or 10 = all-LED blink (shift signal)
byte  3    = velocity high byte   big-endian u16
byte  4    = velocity low byte
byte  5    = gear                 1..9; 0 == neutral / reverse / "no gear"
byte  6..14 = 0x00                padding
```

### C12 — VID `0x3416` / PID `0x1023`, 16-byte wire report (17-byte buffer)

```
byte  0    = 0x00                 HID report ID (stripped before the wire)
byte  1    = 0xFA                 header
byte  2    = 0xFB
byte  3    = 0xD4
byte  4    = RPM percent 0..100   not a count
byte  5    = velocity high byte   big-endian u16
byte  6    = velocity low byte
byte  7    = gear                 1..9; 0 == neutral / reverse / "no gear"
byte  8..16 = 0x00                padding
```

Report construction lives in `Devices/CammusWheelModel.cs` (`BuildC5` / `BuildC12`). Both
are pure functions of `(lit, velocity, gear)` and easy to unit-test without SimHub.

### Field derivation

- **lit-count** — counted in `CammusLedDeviceManager.Display()` from SimHub's computed
  `Color[]`: number of non-black LEDs, weighted by `rpmBrightness`. This is what lets a user
  drive the wheel with SimHub's normal RPM-LED effects UI.
- **C5 blink promotion** — when `lit >= LedCount` (9), byte 1 is forced to `10`, which the
  firmware reads as the all-LED blink / shift signal.
- **C12 percent** — `pct = 100 * lit / LedCount`. `LedCount = 10` is a UI-only assumption
  (see open questions); the firmware itself takes a percentage, not a count.
- **velocity** — `GameData.NewData.SpeedKmh`, clamped to `ushort`, big-endian.
- **gear** — `GameData.NewData.Gear` string parsed to int and sent as-is; `R`/`N`/empty → 0
  (the "no gear" cell). The reference's `gear-1` was an off-by-one bug — 1st gear must send 1,
  not 0.

## SimHub integration

### How LED data reaches the wheel

SimHub owns the LED-effect pipeline. The plugin taps it by injecting a virtual
`ILedDeviceManager` so SimHub computes colors as if a real RGB device were attached, then we
collapse those colors to a lit-count and send our own HID report. The technique is documented
in `../moza-simhub-plugin/docs/simhub.md`; the moving parts here:

1. **`CammusDeviceDefinitionDeployer`** writes the two embedded `device.json` templates to
   `<SimHub>/DevicesDefinitions/User/Cammus C5|C12/device.json` on every `Init()`. SimHub
   reads device templates only at startup, so a first install needs a SimHub restart.
2. **`CammusDeviceExtensionFilter`** (`IDeviceExtensionFilter`) matches a device whose
   `DeviceTypeID` starts with one of our `DescriptorUniqueId` GUIDs and attaches
   `CammusWheelDeviceExtension`.
3. **`CammusWheelDeviceExtension`** defers injection to the first `DataUpdate()` (not
   `Init()`), because `LedModuleDevice.SetSettings()` runs after `Init()` and would clobber
   an early injection. It walks `LinkedDevice.GetInstances()`, finds the `LedModuleDevice`,
   and reflects on the (non-public) `LedModuleSettings.DeviceDriver` setter to install
   `CammusLedDeviceManager`.
4. **`CammusLedDeviceManager.Display()`** materializes the `Color[]`, stores `LastState` (so
   NCalc formulas can read it), counts lit LEDs, and calls `plugin.SendLedUpdate(lit)`.
5. **`CammusPlugin.SendLedUpdate()`** owns the HID write — it builds the report from the
   detected model spec plus cached velocity/gear and writes through `CammusHidConnection`.

The LED manager does **not** write HID directly; it only computes the lit-count and hands it
to the plugin. The plugin is the single owner of the HID stream.

### Why the device.json uses a placeholder VID/PID

The templates declare a **fake** detection VID/PID — C5 `0x9999/0xC5C5`, C12 `0x9999/0xC12C`
— not the real Cammus IDs. This is deliberate.

`device.json` sets `HardwareInterface.TypeName: "LedsStandardHIDProtocol"`, which is SimHub's
stock HID LED driver. If that template carried the **real** VID/PID, SimHub's own driver would
open the wheel and write its standard RGB-per-LED format (report-id + RGB triplets) to it,
racing our writes and putting garbage on the wire. (An early USB capture showed exactly this:
OUT packets with no `0xFC` header.)

By giving SimHub a VID/PID that matches no real device, SimHub's stock driver never opens
anything, stays quiet, and our plugin owns the wire via its own HidSharp handle
(`CammusHidConnection`, which enumerates by the **real** VID/PID from `CammusModelSpec`). The
injected virtual driver exists only to bridge SimHub's computed colors into our pipeline and
to drive the "Connected" UX — it is not the thing that talks to the wheel.

This means there are **two** sources of truth for VID/PID, intentionally:
- `device.json` → placeholder, so SimHub's stock driver stays out of the way.
- `CammusModelSpec` (`Devices/CammusWheelModel.cs`) → real IDs, used by our HID handle.

### Why BA63Driver.dll / SerialDash.dll are referenced

These are SimHub assemblies with vestigial names (BA63 = an old serial display family;
SerialDash = serial dashboards). They are referenced only because `ILedDeviceManager`'s method
signatures use types that live in them (`LedDeviceState`, `IPhysicalMapper`,
`NeutralLedsMapper`, `ILedDriverBase`, `SerialDashController.ScanArgs`). No serial port is
opened — this is a pure USB-HID device. The names are misleading; the dependency is real.

### Reflection fragility

The injection reflects into SimHub internals (`LedModuleSettings.DeviceDriver` non-public
setter, `LedModuleDevice.ledModuleSettings`). Any SimHub release that refactors those internals
can break it silently. Diagnostic logging is in place along the whole chain
(`[Cammus] ExtensionFilter probed` / `MATCH`, `InjectLedDriver: instance #N`) to show where it
breaks. Always build against the actual SimHub runtime DLLs (see `libs/SimHub/`), per
`simhub.md`.

## Project layout

```
CammusPlugin.cs                         IPlugin/IDataPlugin/IWPFSettingsV2 entry point + HID-write owner
Devices/
  CammusWheelModel.cs                   model specs, real VID/PID, BuildC5/BuildC12 report builders
  CammusHidConnection.cs                HidSharp wrapper (enumerate real VID/PID, open, write, reconnect)
  CammusLedDeviceManager.cs             virtual ILedDeviceManager: colors -> lit-count -> plugin
  CammusWheelDeviceExtension.cs         DeviceExtension: reflection injection on first DataUpdate
  CammusDeviceExtensionFilter.cs        IDeviceExtensionFilter: GUID match -> attach extension
  CammusDeviceDefinitionDeployer.cs     extracts embedded device.json to DevicesDefinitions/User
UI/SettingsControl.xaml[.cs]            single-tab status pill + telemetry readout + test button
DeviceTemplates/CammusC5|C12/device.json  embedded SimHub device descriptors (placeholder VID/PID)
libs/SimHub/                            SimHub runtime DLLs to compile against (Private=false)
```

## Build & deploy

- net48, x86, `UseWPF`. SimHub DLL refs are `Private=false` (loaded from the SimHub install).
- `dotnet build -c Release` → `bin/x86/Release/CammusPlugin.dll`. Device templates are
  embedded in the DLL, so the single DLL is the full deployment.
- The csproj `CopyToSimHub` target copies the DLL to `$(SIMHUB_PATH)` when that env var is
  set. The `/deploy` skill drives this.
- CI: `.github/workflows/` — `build.yml`, `dev-build.yml` (pushes to `dev`), `release.yml`
  (tags `v*`), `simhub-update.yml`. No Discord hooks.

## Verifying on the wire

USB capture with usbmon (Linux) or USBPcap (Windows). Expect:
- C5: OUT reports starting `FC ..`, byte 1 tracking lit-count (10 at redline).
- C12: OUT reports starting `FA FB D4 ..`, byte 3 tracking RPM percent.

If you see OUT reports **without** the `0xFC` / `0xFA FB D4` header, SimHub's stock driver is
writing — which means injection failed or the device.json picked up a real VID/PID. Check the
`[Cammus]` log lines.

## Open questions / unverified

- **C12 physical LED count.** monocoque `a810044` does not state one (firmware takes a
  percentage). `device.json` and `CammusModelSpec` both assume **10**. Confirm against
  physical hardware before relying on the C12 percent mapping.
- **Whole protocol** is transcribed from the monocoque commit, not yet confirmed against a
  real Cammus wheel in this repo. Treat byte layouts as best-known, not proven.
- **HID report ID semantics.** HidSharp/Windows treats buffer byte 0 as the report ID and
  strips it before the wire, so the buffer carries a leading `0x00` report ID and the `0xFC`/
  `0xFA` header sits at byte 1 (see protocol section above). Confirm with a capture that the
  wire payload starts with the header byte.
```
