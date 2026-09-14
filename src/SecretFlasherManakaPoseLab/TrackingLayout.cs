namespace SecretFlasherManakaPoseLab;

public static class TrackingLayout
{
    public static readonly string[] Names = { "Head", "Hips", "Left hand", "Right hand", "Left foot", "Right foot", "Left knee", "Right knee", "Left elbow", "Right elbow", "Chest" };
    public static bool IsSupported(int count) => count is 6 or 8 or 10 or 11;
    public static bool IsActive(int count, int index) => IsSupported(count) && index >= 0 && index < count;
    public static int Next(int count) => count switch { 6 => 8, 8 => 10, 10 => 11, _ => 6 };
}
