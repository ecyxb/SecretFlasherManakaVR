using System;
using System.Numerics;

namespace SecretFlasherManakaPoseLab;

// Engine-independent analytical two-bone IK. The pole is a point, not an angle.
public static class PoseMath
{
    public static (Vector3 Joint, Vector3 End) Solve(Vector3 root, Vector3 target,
        Vector3 pole, float upperLength, float lowerLength, Vector3 fallbackDirection, Vector3? fallbackPole = null)
    {
        if (!float.IsFinite(upperLength) || !float.IsFinite(lowerLength) || upperLength < 1e-5f || lowerLength < 1e-5f)
            throw new ArgumentOutOfRangeException(nameof(upperLength), "Bone lengths must be finite and positive.");
        Vector3 delta = target - root;
        Vector3 direction = Unit(delta, Unit(fallbackDirection, Vector3.UnitZ));
        float margin = MathF.Min(1e-4f, MathF.Min(upperLength, lowerLength) * .01f);
        float distance = Math.Clamp(delta.Length(), MathF.Abs(upperLength - lowerLength) + margin,
            upperLength + lowerLength - margin);
        Vector3 bend = pole - root;
        bend -= direction * Vector3.Dot(bend, direction);
        if (bend.LengthSquared() < 1e-8f && fallbackPole.HasValue)
        {
            bend = fallbackPole.Value - root;
            bend -= direction * Vector3.Dot(bend, direction);
        }
        if (bend.LengthSquared() < 1e-8f)
        {
            Vector3 axis = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) < .9f ? Vector3.UnitY : Vector3.UnitX;
            bend = axis - direction * Vector3.Dot(axis, direction);
        }
        bend = Vector3.Normalize(bend);
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
        float height = MathF.Sqrt(MathF.Max(0, upperLength * upperLength - along * along));
        return (root + direction * along + bend * height, root + direction * distance);
    }

    private static Vector3 Unit(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : fallback;
}
