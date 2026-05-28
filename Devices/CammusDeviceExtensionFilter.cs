using System;
using System.Collections.Generic;
using SimHub.Plugins.Devices;
using SimHub.Plugins.Devices.DeviceExtensions;

namespace CammusPlugin.Devices
{
    public sealed class CammusDeviceExtensionFilter : IDeviceExtensionFilter
    {
        public IEnumerable<Type> GetExtensionsTypes(DeviceInstance device)
        {
            var typeId = device.DeviceDescriptor.DeviceTypeID ?? "";
            CammusLog.Debug($"[Cammus] ExtensionFilter probed with DeviceTypeID='{typeId}'");

            if (string.IsNullOrEmpty(typeId)) yield break;

            foreach (var spec in CammusModelSpec.All)
            {
                if (typeId.StartsWith(spec.DescriptorUniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    CammusLog.Info(
                        $"[Cammus] ExtensionFilter MATCH: {spec.DisplayName} (DeviceTypeID='{typeId}') → attaching CammusWheelDeviceExtension");
                    yield return typeof(CammusWheelDeviceExtension);
                    yield break;
                }
            }
        }
    }
}
