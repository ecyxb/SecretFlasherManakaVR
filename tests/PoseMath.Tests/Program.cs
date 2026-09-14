using System.Numerics;
using SecretFlasherManakaPoseLab;
using SecretFlasherManakaVR;

int count = 0;
void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
var root = new Vector3(1, 2, 3);
foreach (float a in new[] { .2f, .3f, .5f })
foreach (float b in new[] { .2f, .35f, .5f })
foreach (var delta in new[] { Vector3.Zero, Vector3.UnitX * .001f, Vector3.UnitY * .4f, Vector3.UnitZ * 3, new Vector3(.3f, -.2f, .1f) })
foreach (var pole in new[] { root, root + delta, root + Vector3.UnitZ, root - Vector3.UnitY })
{
    var solved = PoseMath.Solve(root, root + delta, pole, a, b, Vector3.UnitZ);
    Check(float.IsFinite(solved.Joint.X) && float.IsFinite(solved.Joint.Y) && float.IsFinite(solved.Joint.Z), "Joint must remain finite for collinear/zero targets.");
    Check(Math.Abs(Vector3.Distance(root, solved.Joint) - a) < 2e-4, "Upper bone length changed.");
    Check(Math.Abs(Vector3.Distance(solved.Joint, solved.End) - b) < 2e-4, "Lower bone length changed.");
    float d = delta.Length();
    if (d > Math.Abs(a - b) + .001f && d < a + b - .001f) Check(Vector3.Distance(solved.End, root + delta) < 1e-4, "Reachable target was not reached.");
}
var up = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, Vector3.UnitY, .4f, .4f, Vector3.UnitZ);
var down = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, -Vector3.UnitY, .4f, .4f, Vector3.UnitZ);
Check(up.Joint.Y > 0 && down.Joint.Y < 0, "Pole must determine elbow/knee bend side.");
var again = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, Vector3.UnitY, .4f, .4f, Vector3.UnitZ);
Check(up == again, "Multiple camera passes must not integrate or drift.");
bool rejected = false;
try { PoseMath.Solve(Vector3.Zero, Vector3.One, Vector3.UnitY, 0, .4f, Vector3.UnitZ); }
catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Degenerate skeleton must be rejected.");
foreach (int mode in new[] { 6, 8, 10, 11 })
{
    Check(Enumerable.Range(0, 11).Count(i => TrackingLayout.IsActive(mode, i)) == mode, "Wrong active point count.");
    Check(TrackingLayout.IsActive(mode, 6) == (mode >= 8), "Knees must start at 8 points.");
    Check(TrackingLayout.IsActive(mode, 8) == (mode >= 10), "Elbows must start at 10 points.");
    Check(TrackingLayout.IsActive(mode, 10) == (mode == 11), "Chest must start at 11 points.");
}
Check(!TrackingLayout.IsSupported(7) && !TrackingLayout.IsSupported(12), "Unsupported configurations must be rejected.");
// The joint target selects the closest point on the fixed-length joint circle.
var jointTarget = new Vector3(.2f, .3f, .15f);
var tracked = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, jointTarget, .4f, .4f, Vector3.UnitZ, Vector3.UnitY);
for (int angle = 0; angle < 360; angle += 15)
{
    float radians = angle * MathF.PI / 180;
    var candidate = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0), .4f, .4f, Vector3.UnitZ);
    Check(Vector3.Distance(tracked.Joint, jointTarget) <= Vector3.Distance(candidate.Joint, jointTarget) + 1e-5f, "Tracked joint should be closest while preserving the wrist/ankle target.");
    Check(Vector3.Distance(tracked.End, candidate.End) < 1e-6f, "Bend tracking must not move the wrist/ankle.");
}
var singular = PoseMath.Solve(Vector3.Zero, Vector3.UnitZ * .5f, Vector3.UnitZ, .4f, .4f, Vector3.UnitZ, -Vector3.UnitX);
Check(singular.Joint.X < 0 && MathF.Abs(singular.Joint.Y) < 1e-6f, "Collinear joint tracker must retain the calibrated bend plane.");
foreach (float yaw in new[] { 0f, .5f, MathF.PI / 2, -MathF.PI * .9f })
{
    var forward = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    var inverse = Quaternion.Inverse(forward);
    var origin = new Vector3(2, 1.6f, -3);
    var delta = new Vector3(.12f, -.3f, .4f);
    var measured = origin + Vector3.Transform(delta, forward);
    var mapped = SecretFlasherManakaVR.TrackingSpaceMath.PositionDelta(measured, origin, inverse, .8f);
    Check(Vector3.Distance(mapped, delta * .8f) < 1e-5f, "Calibration must preserve lateral, crouch and forward motion at arbitrary starting yaw.");
    var baseline = forward * Quaternion.CreateFromYawPitchRoll(.2f, -.4f, .1f);
    var desired = Quaternion.CreateFromYawPitchRoll(-.3f, .6f, .5f);
    var measuredRotation = forward * desired * inverse * baseline;
    var mappedRotation = SecretFlasherManakaVR.TrackingSpaceMath.RotationDelta(measuredRotation, baseline, inverse);
    Check(MathF.Abs(Quaternion.Dot(mappedRotation, desired)) > .99999f, "Controller rotation delta must use the calibrated frame, including nonzero initial pitch and roll.");
    var neutral = SecretFlasherManakaVR.TrackingSpaceMath.RotationDelta(baseline, baseline, inverse);
    Check(MathF.Abs(neutral.W) > .99999f, "Calibration must give neutral bone rotation.");
    var translated = SecretFlasherManakaVR.TrackingSpaceMath.PositionDelta(measured + Vector3.One * 7, origin + Vector3.One * 7, inverse, .8f);
    Check(Vector3.Distance(translated, mapped) < 1e-5f, "Tracking-origin translation must cancel without camera feedback.");
}
// Regression: standing hands must not be mapped onto an avatar's crossed-arm animation.
foreach (float yaw in new[] { 0f, MathF.PI / 2, -1.2f })
{
    var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    var inverse = Quaternion.Inverse(facing);
    var headset = new Vector3(3, 1.7f, -2);
    var avatarEye = new Vector3(0, 1.4f, .075f);
    var leftRelative = new Vector3(-.35f, -.8f, 0);
    var rightRelative = new Vector3(.35f, -.8f, 0);
    var physicalLeft = headset + Vector3.Transform(leftRelative, facing);
    var physicalRight = headset + Vector3.Transform(rightRelative, facing);
    var huggedWrist = new Vector3(.08f, 1.1f, .2f);
    var hangingWrist = new Vector3(-.3f, .55f, 0);
    var a = huggedWrist + SecretFlasherManakaVR.TrackingSpaceMath.HandPositionOffset(physicalLeft, headset, inverse, .8f, avatarEye, huggedWrist);
    var b = hangingWrist + SecretFlasherManakaVR.TrackingSpaceMath.HandPositionOffset(physicalLeft, headset, inverse, .8f, avatarEye, hangingWrist);
    Check(Vector3.Distance(a, b) < 1e-5f, "Same physical hand must have the same target regardless of crossed-arm or hanging animation.");
    Check(Vector3.Distance(a, avatarEye + leftRelative * .8f) < 1e-5f, "Wrist target must preserve the physical hand-to-HMD vector.");
    var right = huggedWrist + SecretFlasherManakaVR.TrackingSpaceMath.HandPositionOffset(physicalRight, headset, inverse, .8f, avatarEye, huggedWrist);
    Check(Math.Abs(right.X - a.X - .56f) < 1e-5f && a.Y < 1f, "Hands-down calibration must separate the wrists below the chest, not retain a hug.");
    var motion = new Vector3(.1f, .2f, -.15f);
    var moved = huggedWrist + SecretFlasherManakaVR.TrackingSpaceMath.HandPositionOffset(physicalLeft + Vector3.Transform(motion, facing), headset, inverse, .8f, avatarEye, huggedWrist);
    Check(Vector3.Distance(moved - a, motion * .8f) < 1e-5f, "Physical hand movement must use the shared tracking scale.");
}
// Stereo regression: reconstruct physical height/depth from the two eye rays.
// With unscaled IPD and scaled hand positions, the previous implementation made
// a controller below eye level appear too close and too high.
foreach (float worldScale in new[] { .5f, .823f, 1f, 1.3f })
foreach (var hand in new[] { new Vector3(.2f, -.6f, .45f), new Vector3(-.3f, -.25f, .7f) })
{
    const float deviceIpd = .064f;
    var renderedHand = hand * worldScale;
    float eyeSeparation = SecretFlasherManakaVR.TrackingSpaceMath.EyeSeparation(deviceIpd, 1, worldScale);
    float leftSlope = (renderedHand.X + eyeSeparation / 2) / renderedHand.Z;
    float rightSlope = (renderedHand.X - eyeSeparation / 2) / renderedHand.Z;
    float perceivedDepth = deviceIpd / (leftSlope - rightSlope);
    float perceivedHeight = renderedHand.Y / renderedHand.Z * perceivedDepth;
    Check(Math.Abs(perceivedDepth - hand.Z) < 1e-5f, "Scaled tracking and stereo must preserve perceived physical controller depth.");
    Check(Math.Abs(perceivedHeight - hand.Y) < 1e-5f, "Scaled tracking and stereo must preserve perceived physical controller height.");
}
// Regression: tracked modes ignore the legacy 5 cm clamp when entering seated then standing.
foreach (float yaw in new[] { 0f, 1.3f, -2f })
foreach (var headMotion in new[] { Vector3.Zero, new Vector3(0, .6f, 0), new Vector3(.2f, -.45f, .3f) })
{
    var cameraBase = new Vector3(2, 1.3f, -1);
    var basis = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    var viewHead = TrackingSpaceMath.CameraHeadOffset(BodyTrackingMode.ThreePoint, headMotion, true, new Vector3(-.05f), new Vector3(.05f));
    var handFromHead = new Vector3(.25f, -.55f, .4f);
    var view = cameraBase + Vector3.Transform(viewHead, basis);
    var mappedHead = cameraBase + Vector3.Transform(headMotion, basis);
    var cursor = cameraBase + Vector3.Transform(headMotion + handFromHead, basis);
    Check(Vector3.Distance(mappedHead, view) < 1e-5f, "Controller tracking origin must agree with the rendered head despite legacy clamp configuration.");
    Check(Vector3.Distance(cursor - view, Vector3.Transform(handFromHead, basis)) < 1e-5f, "Sitting, standing and crouching must preserve cursor-to-head displacement.");
    Check(Vector3.Distance(viewHead, headMotion) < 1e-6f, "Tracked camera must preserve full positional movement.");
}
// Mode transitions must not silently revive legacy bone writers after F6, loss or recenter.
foreach (var mode in Enum.GetValues<BodyTrackingMode>())
{
    bool legacy = mode == BodyTrackingMode.Legacy;
    Check(TrackingModePolicy.AllowsLegacyBones(mode) == legacy, "Only the explicitly selected legacy profile may write legacy head/body offsets.");
    Check(TrackingModePolicy.IsSupported(mode) == (legacy || mode == BodyTrackingMode.ThreePoint), "Reserved hardware layouts must never silently fall back to three points.");
    Check(TrackingModePolicy.CanCalibrate(mode) == (mode == BodyTrackingMode.ThreePoint), "Calibration must require a supported device layout.");
    foreach (bool oldClamp in new[] { false, true })
    {
        var standingMotion = new Vector3(.3f, .6f, -.2f);
        var offset = TrackingSpaceMath.CameraHeadOffset(mode, standingMotion, oldClamp, new Vector3(-.05f), new Vector3(.05f));
        var expected = legacy && oldClamp ? new Vector3(.05f, .05f, -.05f) : standingMotion;
        Check(Vector3.Distance(offset, expected) < 1e-6f, "Legacy preserves its clamp; every tracked profile preserves full seated-to-standing displacement, including before calibration.");
    }
    foreach (var previous in Enum.GetValues<BodyTrackingMode>())
    {
        Check(TrackingModePolicy.NeedsViewReset(previous, mode, true, false), "Releasing a tracked body must clear the camera's old calibration/scale basis.");
        Check(TrackingModePolicy.NeedsViewReset(previous, mode, false, false) == (previous != mode), "Only a changed profile should reset an idle view.");
        Check(TrackingModePolicy.NeedsViewReset(previous, mode, false, true) == (previous != mode), "Successful calibration must not immediately be released by a camera reset.");
    }
}
Check(!TrackingModePolicy.IsKnown((BodyTrackingMode)7) && !TrackingModePolicy.CanCalibrate((BodyTrackingMode)7), "Invalid layouts must not start a solver.");
Console.WriteLine($"PASS: {count} assertions (IK, calibration, stereo, tracking profiles and camera transitions).");
