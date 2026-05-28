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

            // Injection deferred to DataUpdate so LedModuleDevice.SetSettings() runs first.
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
            if (_spec == null)
            {
                CammusLog.Warn("[Cammus] InjectLedDriver: _spec is null (DeviceTypeID didn't resolve to a known model)");
                return;
            }

            try
            {
                int instanceCount = 0;
                int ledModuleSeen = 0;
                foreach (var instance in LinkedDevice.GetInstances())
                {
                    instanceCount++;
                    var instType = instance?.GetType().FullName ?? "<null>";
                    CammusLog.Debug($"[Cammus] InjectLedDriver: instance #{instanceCount} = {instType}");

                    if (instance is LedModuleDevice lmd)
                    {
                        ledModuleSeen++;
                        if (lmd.ledModuleSettings == null)
                        {
                            CammusLog.Warn("[Cammus] InjectLedDriver: found LedModuleDevice but ledModuleSettings is null");
                            continue;
                        }

                        var settingsType = lmd.ledModuleSettings.GetType();
                        _ledDriver = new CammusLedDeviceManager(_spec)
                        {
                            LedModuleSettings = lmd.ledModuleSettings
                        };

                        var prop = settingsType.GetProperty(
                            "DeviceDriver",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (prop == null)
                        {
                            CammusLog.Warn(
                                $"[Cammus] InjectLedDriver: LedModuleSettings (runtime type {settingsType.FullName}) has no public 'DeviceDriver' property");
                            return;
                        }

                        var setter = prop.GetSetMethod(nonPublic: true);
                        if (setter == null)
                        {
                            CammusLog.Warn(
                                $"[Cammus] InjectLedDriver: 'DeviceDriver' property has no setter (CanRead={prop.CanRead}, CanWrite={prop.CanWrite}, propType={prop.PropertyType.FullName})");
                            return;
                        }

                        setter.Invoke(lmd.ledModuleSettings, new object[] { _ledDriver });
                        _driverInjected = true;
                        CammusLog.Info(
                            $"[Cammus] Injected virtual LED driver for {_spec.DisplayName} (settingsType={settingsType.FullName})");
                        return;
                    }
                }

                CammusLog.Debug(
                    $"[Cammus] InjectLedDriver: walked {instanceCount} instance(s), {ledModuleSeen} were LedModuleDevice — will retry next frame");
            }
            catch (Exception ex)
            {
                CammusLog.Error($"[Cammus] InjectLedDriver threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // SimHub appends _UserProject / _Embedded suffixes; strip before GUID lookup.
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
