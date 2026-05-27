using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;
using GameReaderCommon;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;
using SimHub.Plugins.Devices.DeviceExtensions;
using SimHub.Plugins.OutputPlugins.GraphicalDash.LedModules;

namespace CammusPlugin.Devices
{
    /// <summary>
    /// Per-device-instance extension. Resolves the matching CammusModelSpec
    /// from the device's DescriptorUniqueId, then on the first DataUpdate
    /// (NOT Init — see simhub.md line 692 about LedModuleDevice.SetSettings
    /// running after Init) injects a CammusLedDeviceManager into the
    /// underlying LedModuleSettings.DeviceDriver so SimHub's effects
    /// pipeline flows into our HID write path.
    /// </summary>
    internal sealed class CammusWheelDeviceExtension : DeviceExtension
    {
        private CammusLedDeviceManager? _ledDriver;
        private bool _driverInjected;
        private CammusModelSpec? _spec;

        public override string ExtentionTabTitle => "Cammus";

        public override void Init(PluginManager pluginManager)
        {
            _spec = CammusModelSpec.FromDescriptorUniqueId(
                StripDeviceTypeIdSuffix(LinkedDevice.DeviceDescriptor.DeviceTypeID));

            if (_spec == null)
            {
                CammusLog.Warn(
                    $"[Cammus] Extension attached to unknown DeviceTypeID '{LinkedDevice.DeviceDescriptor.DeviceTypeID}'");
            }
            else
            {
                CammusLog.Debug($"[Cammus] Extension Init: {_spec.DisplayName}");
            }

            // Injection deferred to DataUpdate — calling here precedes
            // LedModuleDevice.SetSettings(), which would throw on lookup.
        }

        public override void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!_driverInjected) InjectLedDriver();
            _ledDriver?.UpdateConnectionState();
        }

        public override void End(PluginManager pluginManager)
        {
            CammusLog.Debug("[Cammus] Extension End");
        }

        private void InjectLedDriver()
        {
            if (_driverInjected) return;
            if (_spec == null) return;

            try
            {
                foreach (var instance in LinkedDevice.GetInstances())
                {
                    if (instance is LedModuleDevice lmd && lmd.ledModuleSettings != null)
                    {
                        _ledDriver = new CammusLedDeviceManager(_spec)
                        {
                            LedModuleSettings = lmd.ledModuleSettings
                        };

                        var prop = typeof(LedModuleSettings).GetProperty(
                            "DeviceDriver",
                            BindingFlags.Public | BindingFlags.Instance);

                        var setter = prop?.GetSetMethod(nonPublic: true);
                        if (setter != null)
                        {
                            setter.Invoke(lmd.ledModuleSettings, new object[] { _ledDriver });
                            _driverInjected = true;
                            CammusLog.Info(
                                $"[Cammus] Injected virtual LED driver for {_spec.DisplayName}");
                        }
                        else
                        {
                            CammusLog.Warn("[Cammus] LedModuleSettings.DeviceDriver setter not found");
                        }
                        return;
                    }
                }
                CammusLog.Debug("[Cammus] No LedModuleDevice on this instance yet — will retry next frame");
            }
            catch (Exception ex)
            {
                CammusLog.Error($"[Cammus] InjectLedDriver threw: {ex.Message}");
            }
        }

        // SimHub may append _UserProject / _Embedded to template-based device
        // type IDs (simhub.md line 699). Strip so spec lookup matches the
        // raw GUID we ship in device.json.
        private static string? StripDeviceTypeIdSuffix(string? typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return typeId;
            int underscore = typeId!.IndexOf('_');
            return underscore > 0 ? typeId.Substring(0, underscore) : typeId;
        }

        public override void LoadDefaultSettings() { }

        public override JToken GetSettings() => new JObject();

        public override void SetSettings(JToken settings, bool isDefault) { }

        public override Control CreateSettingControl()
        {
            // The plugin already exposes a top-level settings tab; the per-
            // device extension tab is intentionally minimal. Label (a
            // ContentControl) is the lightest Control-derived host for a
            // single line of text — TextBlock would be lighter but isn't a
            // Control.
            return new Label
            {
                Content = _spec?.DisplayName ?? "Cammus device",
                Margin = new System.Windows.Thickness(12)
            };
        }

        public override IEnumerable<DynamicButtonAction> GetDynamicButtonActions()
        {
            yield break;
        }
    }
}
