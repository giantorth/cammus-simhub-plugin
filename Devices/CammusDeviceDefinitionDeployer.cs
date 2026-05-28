using System;
using System.IO;
using System.Reflection;

namespace CammusPlugin.Devices
{
    internal static class CammusDeviceDefinitionDeployer
    {
        private const string ResC5 = "CammusPlugin.Devices.CammusC5.device.json";
        private const string ResC12 = "CammusPlugin.Devices.CammusC12.device.json";

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
                    // Skip rewrite when descriptor matches to avoid spurious "restart SimHub" prompts.
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
