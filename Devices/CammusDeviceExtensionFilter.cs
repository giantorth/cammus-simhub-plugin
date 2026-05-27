using System;
using System.Collections.Generic;
using SimHub.Plugins.Devices;
using SimHub.Plugins.Devices.DeviceExtensions;

namespace CammusPlugin.Devices
{
    /// <summary>
    /// SimHub discovers this via assembly scanning; tells SimHub to attach
    /// CammusWheelDeviceExtension to any Cammus wheel device instance.
    /// Matches by DescriptorUniqueId (set in the embedded device.json
    /// templates) — covers both raw GUID and SimHub's suffixed variants
    /// (see simhub.md line 699).
    /// </summary>
    public sealed class CammusDeviceExtensionFilter : IDeviceExtensionFilter
    {
        public IEnumerable<Type> GetExtensionsTypes(DeviceInstance device)
        {
            var typeId = device.DeviceDescriptor.DeviceTypeID ?? "";
            if (string.IsNullOrEmpty(typeId)) yield break;

            foreach (var spec in CammusModelSpec.All)
            {
                if (typeId.StartsWith(spec.DescriptorUniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    yield return typeof(CammusWheelDeviceExtension);
                    yield break;
                }
            }
        }
    }
}
