using System;
using BepInEx.Logging;

namespace SecretFlasherManakaVR.Runtime
{
    public interface IVrRuntimeLogger
    {
        void Info(string message);

        void Warning(string message);

        void Error(string message);

        void Error(string message, Exception exception);
    }

    internal sealed class NullVrRuntimeLogger : IVrRuntimeLogger
    {
        public static readonly NullVrRuntimeLogger Instance = new NullVrRuntimeLogger();

        private NullVrRuntimeLogger()
        {
        }

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }

    internal sealed class BepInExVrRuntimeLogger : IVrRuntimeLogger
    {
        private readonly ManualLogSource log;

        public BepInExVrRuntimeLogger(ManualLogSource log)
        {
            this.log = log;
        }

        public void Info(string message)
        {
            log.LogInfo(message);
        }

        public void Warning(string message)
        {
            log.LogWarning(message);
        }

        public void Error(string message)
        {
            log.LogError(message);
        }

        public void Error(string message, Exception exception)
        {
            log.LogError($"{message} {exception}");
        }
    }
}
