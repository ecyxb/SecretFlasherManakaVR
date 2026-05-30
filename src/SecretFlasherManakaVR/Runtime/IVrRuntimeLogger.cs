namespace SecretFlasherManakaVR.Runtime
{
    public interface IVrRuntimeLogger
    {
    }

    internal sealed class NullVrRuntimeLogger : IVrRuntimeLogger
    {
        public static readonly NullVrRuntimeLogger Instance = new NullVrRuntimeLogger();

        private NullVrRuntimeLogger()
        {
        }

    }
}
