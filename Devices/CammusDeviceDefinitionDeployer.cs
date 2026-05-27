using System;
using System.IO;
using System.Reflection;

namespace CammusPlugin.Devices
{
    /// <summary>
    /// Extracts the embedded device.json templates and writes them into
    /// <c>{SimHub}/DevicesDefinitions/User/Cammus C5/device.json</c> and
    /// <c>…/Cammus C12/device.json</c>. Idempotent: skips writes when the
    /// existing file has the matching DescriptorUniqueId, so a SimHub
    /// restart isn't needed unless the template itself changed. Re-deploys
    /// on every plugin Init because SimHub deletes the file when a user
    /// removes the device (simhub.md line 696).
    /// </summary>
    internal static class CammusDeviceDefinitionDeployer
    {
        private const string ResC5 = "CammusPlugin.Devices.CammusC5.device.json";
        private const string ResC12 = "CammusPlugin.Devices.CammusC12.device.json";

        /// <summary>
        /// Deploy every Cammus device.json template unconditionally. SimHub
        /// only instantiates a device when its VID/PID matches a USB-attached
        /// device, so having both templates on disk is harmless — and it
        /// lets a developer without hardware inspect what got written, and
        /// lets SimHub recognize a wheel the moment it's hot-plugged with
        /// no plugin restart required.
        /// </summary>
        public static void DeployAll()
        {
            DeployOne(CammusModelSpec.C5, ResC5);
            DeployOne(CammusModelSpec.C12, ResC12);
        }

        private static void DeployOne(CammusModelSpec spec, string resourceName)
        {
            try
            {
                var simHubDir = AppDomain.CurrentDomain.BaseDirectory;
                var targetDir = Path.Combine(simHubDir, "DevicesDefinitions", "User", spec.DeviceFolderName);
                var targetPath = Path.Combine(targetDir, "device.json");

                if (File.Exists(targetPath))
                {
                    // Skip rewrite when the descriptor matches — avoids the
                    // "restart SimHub" prompt on every plugin reload. A
                    // mismatch (or a parse failure) is rewrite-worthy.
                    try
                    {
                        var existing = File.ReadAllText(targetPath);
                        if (existing.IndexOf(spec.DescriptorUniqueId, StringComparison.OrdinalIgnoreCase) >= 0)
                            return;
                    }
                    catch (Exception ex)
                    {
                        CammusLog.Warn(
                            $"[Cammus] Could not read existing device.json for {spec.DisplayName}, rewriting: {ex.Message}");
                    }
                }

                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        CammusLog.Warn($"[Cammus] Embedded resource missing: {resourceName}");
                        return;
                    }

                    Directory.CreateDirectory(targetDir);
                    string json;
                    using (var reader = new StreamReader(stream))
                    {
                        json = reader.ReadToEnd();
                    }
                    File.WriteAllText(targetPath, json);
                }

                CammusLog.Info(
                    $"[Cammus] Deployed device definition: {spec.DisplayName} → {targetPath} (restart SimHub if first install)");
            }
            catch (Exception ex)
            {
                CammusLog.Error($"[Cammus] Error deploying {spec.DisplayName}: {ex.Message}");
            }
        }
    }
}
