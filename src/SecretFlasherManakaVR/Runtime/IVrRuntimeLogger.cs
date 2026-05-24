using System;

namespace SecretFlasherManakaVR.Runtime
{
    public interface IVrRuntimeLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
        void Debug(string message);
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

        public void Debug(string message)
        {
        }
    }

    internal sealed class RateLimitedLogger
    {
        private readonly IVrRuntimeLogger logger;
        private readonly float intervalSeconds;
        private readonly System.Collections.Generic.Dictionary<string, float> nextLogTimes =
            new System.Collections.Generic.Dictionary<string, float>();

        public RateLimitedLogger(IVrRuntimeLogger logger, float intervalSeconds)
        {
            this.logger = logger ?? NullVrRuntimeLogger.Instance;
            this.intervalSeconds = Math.Max(0.25f, intervalSeconds);
        }

        public void Info(string key, string message)
        {
            if (!CanLog(key))
            {
                return;
            }

            logger.Info(message);
        }

        public void Warning(string key, string message)
        {
            if (!CanLog(key))
            {
                return;
            }

            logger.Warning(message);
        }

        public void Error(string key, string message)
        {
            if (!CanLog(key))
            {
                return;
            }

            logger.Error(message);
        }

        private bool CanLog(string key)
        {
            float now = UnityEngine.Time.unscaledTime;
            float next;
            if (nextLogTimes.TryGetValue(key, out next) && now < next)
            {
                return false;
            }

            nextLogTimes[key] = now + intervalSeconds;
            return true;
        }
    }
}
