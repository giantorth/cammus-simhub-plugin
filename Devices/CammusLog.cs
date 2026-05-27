namespace CammusPlugin
{
    /// <summary>
    /// Thin wrapper over SimHub.Logging.Current so a SimHub-less unit test
    /// or design-time host doesn't NRE on the first log call.
    /// </summary>
    internal static class CammusLog
    {
        public static void Info(string message)
        {
            try { SimHub.Logging.Current.Info(message); } catch { }
        }

        public static void Debug(string message)
        {
            try { SimHub.Logging.Current.Debug(message); } catch { }
        }

        public static void Warn(string message)
        {
            try { SimHub.Logging.Current.Warn(message); } catch { }
        }

        public static void Error(string message)
        {
            try { SimHub.Logging.Current.Error(message); } catch { }
        }
    }
}
