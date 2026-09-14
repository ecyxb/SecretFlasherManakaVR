using System.Text.Json;
using BepInEx;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecretFlasherManakaPoseLab;

public sealed class LabControl
{
    public bool Enabled { get; set; }
    public bool Demo { get; set; } = true;
    public bool Preview { get; set; } = true;
    public bool ShowTargets { get; set; } = true;
    public float CameraYaw { get; set; }
    public TrackingFrame Frame { get; set; } = new();
}

public sealed class PoseLab : MonoBehaviour
{
    internal static PoseLab? Instance;
    private readonly FakeTrackingInput input = new();
    private LabControl control = new();
    private AvatarRig? rig;
    private TrackingFrame sampled = new();
    private Camera? preview;
    private readonly List<GameObject> markers = new();
    private readonly List<PoseCameraHook> cameraHooks = new();
    private readonly List<(Canvas Canvas, bool Enabled)> hiddenCanvases = new();
    private readonly List<(GameObject Object, int Layer)> previewLayers = new();
    private int previewLayer = -1;
    private Material? markerMaterial;
    private string folder = "", commandPath = "", message = "Enter gameplay, then enable control (F6).";
    private DateTime commandWrite;
    private float nextPoll, nextBind, nextStatus, demoStarted;
    private bool panel = true, acquiredCursor, oldVisible, applying, stopped;
    private CursorLockMode oldCursorLock;
    private int targetIndex, boneIndex;
    private bool tracedApply;
    private GUIStyle? labelStyle;
    private int boneGroup;
    private readonly string[] boneGroups = { "All bones", "Head / neck", "Spine / hips", "Arms / hands", "Fingers", "Legs / feet" };
    private readonly string[] targetNames = TrackingLayout.Names;

    public PoseLab(IntPtr ptr) : base(ptr) { }

    private void Start()
    {
        Instance = this;
        folder = Path.Combine(Paths.ConfigPath, "ManakaPoseLab");
        Directory.CreateDirectory(folder);
        commandPath = Path.Combine(folder, "input.json");
        if (!File.Exists(commandPath)) SaveInput();
        demoStarted = Time.unscaledTime;
    }

    private void Update()
    {
        if (stopped || Instance != this) return;
        try
        {
            if (Input.GetKeyDown(KeyCode.F6)) Toggle();
            if (Input.GetKeyDown(KeyCode.F7)) { control.Demo = !control.Demo; demoStarted = Time.unscaledTime; }
            if (Input.GetKeyDown(KeyCode.F8)) control.Preview = !control.Preview;
            if (Input.GetKeyDown(KeyCode.F9)) ResetInput();
            if (Input.GetKeyDown(KeyCode.F10)) panel = !panel;
            if (Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime + .5f; PollInput(); }
            var player = PlayerController.Instance;
            if (rig != null && (!control.Enabled || player == null || rig.Player != player || rig.Head == null || !player.gameObject.activeInHierarchy)) Release();
            if (control.Enabled && rig == null && player != null && player.gameObject.activeInHierarchy && Time.unscaledTime >= nextBind)
            {
                nextBind = Time.unscaledTime + 2;
                TryAcquire(player);
            }
            input.Demo = control.Demo; input.Manual = control.Frame;
            sampled = input.Sample(Time.unscaledTime - demoStarted);
            UpdateCursor();
            if (preview != null) preview.enabled = rig != null && control.Preview;
            if (rig != null && Time.unscaledTime >= nextStatus)
            {
                nextStatus = Time.unscaledTime + 2;
                // Apply through the game dispatcher and the dedicated preview camera.
                // Avoid adding injected behaviours to the game's dynamically recycled cameras.
                WriteStatus();
            }
        }
        catch (Exception e) { Fail(e); }
    }

    private void LateUpdate()
    {
        SafeApply();
        UpdatePreview();
        // Keep cursor usable after the game's own input loop.
        if (acquiredCursor) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    internal void SafeApply()
    {
        if (stopped || rig == null || !control.Enabled || applying) return;
        applying = true;
        try
        {
            if (!tracedApply) Plugin.Logger.LogInfo("Pose diagnostics: first solve begins.");
            rig.Apply(sampled);
            if (!tracedApply) Plugin.Logger.LogInfo("Pose diagnostics: first solve completed.");
            UpdateMarkers();
            tracedApply = true;
        }
        catch (Exception e) { Fail(e); }
        finally { applying = false; }
    }

    private void TryAcquire(PlayerController player)
    {
        AvatarRig? candidate = null;
        try
        {
            candidate = new AvatarRig(player);
            candidate.Acquire();
            rig = candidate;
            tracedApply = false;
            demoStarted = Time.unscaledTime;
            message = $"Controlling {rig.Bones.Count} bones. Root motion is not tracked.";
            ExportSkeleton();
            CreatePreview();
            Plugin.Logger.LogInfo($"Pose control acquired: player={player.name}, animator={rig.Animator.name}, bones={rig.Bones.Count}.");
        }
        catch (Exception e)
        {
            candidate?.Release(); rig = null;
            control.Enabled = false;
            message = "Cannot bind: " + e.Message;
            Plugin.Logger.LogError(message + "\n" + e);
        }
    }

    private void Release()
    {
        var previous = rig; rig = null;
        try { previous?.Release(); }
        finally
        {
            if (preview != null) preview.enabled = false;
            foreach (var m in markers) if (m != null) m.SetActive(false);
            RestoreGameUi();
            RestorePreviewLayers();
            RestoreCursor();
        }
        if (previous != null) Plugin.Logger.LogInfo("Pose control released; calibrated bones, animator, rig and physics flags restored.");
        if (!string.IsNullOrEmpty(folder)) AtomicWrite(Path.Combine(folder, "status.json"), new { Time = DateTime.UtcNow, Active = false, Restored = true });
    }

    private void Fail(Exception e)
    {
        control.Enabled = false;
        try { Release(); } catch (Exception restore) { Plugin.Logger.LogWarning("Restore: " + restore.Message); }
        message = "Stopped: " + e.Message;
        Plugin.Logger.LogError(message + "\n" + e);
    }

    private void Toggle()
    {
        control.Enabled = !control.Enabled;
        if (!control.Enabled) { Release(); message = "Control off; game animation restored."; }
    }
    private void ResetInput() { control.Frame = new() { TrackingPoints = control.Frame.TrackingPoints, PelvisFollowsHead = control.Frame.PelvisFollowsHead }; control.Demo = false; message = "Neutral calibrated pose."; }
    private void UpdateCursor()
    {
        bool want = panel && rig != null;
        if (want && !acquiredCursor) { oldCursorLock = Cursor.lockState; oldVisible = Cursor.visible; acquiredCursor = true; }
        if (!want) RestoreCursor();
    }
    private void RestoreCursor()
    {
        if (!acquiredCursor) return;
        acquiredCursor = false; Cursor.lockState = oldCursorLock; Cursor.visible = oldVisible;
    }

    private void PollInput()
    {
        if (!File.Exists(commandPath)) return;
        DateTime write = File.GetLastWriteTimeUtc(commandPath);
        if (write == commandWrite) return;
        try
        {
            var next = JsonSerializer.Deserialize<LabControl>(File.ReadAllText(commandPath), FakeTrackingInput.Json)
                ?? throw new InvalidDataException("Empty control input.");
            next.Frame ??= new(); next.Frame.Sanitize();
            next.CameraYaw = TargetOffset.Limit(next.CameraYaw, -180, 180);
            control = next; commandWrite = write;
            message = "Loaded input.json. " + (next.Demo ? "Demo tracking." : "Manual tracking.");
        }
        catch (Exception e) { commandWrite = write; message = "Input rejected; previous pose retained: " + e.Message; Plugin.Logger.LogWarning(message); }
    }
    private void SaveInput()
    {
        AtomicWrite(commandPath, control);
        commandWrite = File.GetLastWriteTimeUtc(commandPath);
        message = "Saved input.json; edits reload automatically.";
    }
    private static void AtomicWrite(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, FakeTrackingInput.Json));
        File.Move(path + ".tmp", path, true);
    }
    private void ExportSkeleton()
    {
        if (rig == null) return;
        AtomicWrite(Path.Combine(folder, "skeleton.json"), rig.Bones.Select(b => new { b.Path, Name = b.Transform.name,
            LocalPosition = V(b.Position), LocalRotation = Q(b.Rotation), LocalScale = V(b.Scale) }).ToArray());
    }
    private void WriteStatus()
    {
        if (rig == null) return;
        AtomicWrite(Path.Combine(folder, "status.json"), new { Time = DateTime.UtcNow, Active = true, control.Demo,
            TrackingPoints = rig.AppliedTrackingPoints,
            BoneCount = rig.Bones.Count, rig.Height, HeadErrorMetres = rig.HeadError, MaxLimbErrorMetres = rig.MaxLimbError,
            ChestErrorMetres = rig.AppliedTrackingPoints == 11 ? (float?)rig.ChestError : null,
            ChestRotationErrorDegrees = rig.AppliedTrackingPoints == 11 ? (float?)rig.ChestRotationError : null,
            Tracking = Enumerable.Range(0, targetNames.Length).Select(i => new { Name = targetNames[i],
                Enabled = TrackingLayout.IsActive(rig.AppliedTrackingPoints, i),
                Constraint = i is >= 6 and <= 9 ? "bend-position" : "position-and-rotation",
                Requested = V(rig.Targets[i]), Actual = V(rig.ActualPoints[i]),
                ErrorMetres = TrackingLayout.IsActive(rig.AppliedTrackingPoints, i) ? (float?)rig.PointErrors[i] : null }).ToArray(),
            Head = V(rig.Head.position), Hips = V(rig.Hips.position), Targets = rig.Targets.Select(V).ToArray(),
            Bones = rig.Bones.Select(b => new { b.Path, Position = V(b.Transform.position), Rotation = Q(b.Transform.localRotation) }).ToArray() });
    }
    private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };

    private void CreatePreview()
    {
        if (preview == null)
        {
            var go = new GameObject("ManakaPoseLab.Preview"); Object.DontDestroyOnLoad(go);
            preview = go.AddComponent<Camera>();
            if (Camera.main != null) preview.CopyFrom(Camera.main);
            preview.targetTexture = null; preview.stereoTargetEye = StereoTargetEyeMask.None;
            preview.depth = 100; preview.cullingMask = -1; preview.nearClipPlane = .03f;
            preview.fieldOfView = 48; preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = new Color(.11f, .14f, .19f);
            cameraHooks.Add(go.AddComponent<PoseCameraHook>());
        }
        if (markers.Count == 0)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null) markerMaterial = new Material(shader);
            var colors = new[] { Color.white, Color.yellow, Color.cyan, Color.red, Color.blue, new Color(1, .4f, .1f),
                new Color(.3f, 1, .5f), new Color(.7f, 1, .2f), new Color(.8f, .3f, 1), new Color(1, .4f, .8f), new Color(1, .7f, .1f) };
            for (int i = 0; i < targetNames.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Pose target " + targetNames[i]; marker.layer = 2;
                Object.DontDestroyOnLoad(marker);
                var collider = marker.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Object.Destroy(collider); }
                marker.transform.localScale = Vector3.one * .045f;
                if (markerMaterial != null)
                {
                    var renderer = marker.GetComponent<Renderer>();
                    renderer.sharedMaterial = markerMaterial;
                    var block = new MaterialPropertyBlock(); block.SetColor("_Color", colors[i]); renderer.SetPropertyBlock(block);
                }
                markers.Add(marker);
            }
        }
    }
    private void UpdateMarkers()
    {
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] == null) continue;
            markers[i].SetActive(rig != null && control.ShowTargets && TrackingLayout.IsActive(rig.AppliedTrackingPoints, i));
            if (rig != null) markers[i].transform.position = rig.Targets[i];
        }
    }
    private void UpdatePreview()
    {
        if (rig == null || preview == null || !control.Preview) { RestoreGameUi(); RestorePreviewLayers(); return; }
        if (previewLayer < 0)
        {
            var occupied = new HashSet<int>();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>(true)) occupied.Add(renderer.gameObject.layer);
            for (int layer = 31; layer >= 20; layer--) if (!occupied.Contains(layer)) { previewLayer = layer; break; }
            if (previewLayer >= 0)
            {
                var objects = new HashSet<int>();
                foreach (var renderer in rig.Root.GetComponentsInChildren<Renderer>(true))
                    if (objects.Add(renderer.gameObject.GetInstanceID())) previewLayers.Add((renderer.gameObject, renderer.gameObject.layer));
            }
        }
        foreach (var item in previewLayers) if (item.Object != null) item.Object.layer = previewLayer;
        foreach (var marker in markers) if (marker != null) marker.layer = previewLayer >= 0 ? previewLayer : 2;
        preview.cullingMask = previewLayer >= 0 ? 1 << previewLayer : -1;
        if (hiddenCanvases.Count == 0)
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                if (canvas.renderMode != RenderMode.WorldSpace) hiddenCanvases.Add((canvas, canvas.enabled));
        foreach (var canvas in hiddenCanvases) if (canvas.Canvas != null) canvas.Canvas.enabled = false;
        float panelScale = Mathf.Min(Screen.height / 1020f, 1.25f);
        float width = panel ? Mathf.Clamp(410f * panelScale / Screen.width, .2f, .5f) : 0;
        preview.rect = new Rect(width, 0, 1 - width, 1);
        var look = rig.Root.position + Vector3.up * (rig.Height * .55f);
        var yaw = rig.FrameRotation * Quaternion.Euler(0, control.CameraYaw, 0);
        preview.transform.position = look + yaw * new Vector3(0, .08f, rig.Height * 2.1f);
        preview.transform.LookAt(look);
    }
    private void RestoreGameUi()
    {
        foreach (var canvas in hiddenCanvases) if (canvas.Canvas != null) canvas.Canvas.enabled = canvas.Enabled;
        hiddenCanvases.Clear();
    }
    private void RestorePreviewLayers()
    {
        foreach (var item in previewLayers) if (item.Object != null) item.Object.layer = item.Layer;
        foreach (var marker in markers) if (marker != null) marker.layer = 2;
        previewLayers.Clear(); previewLayer = -1;
    }

    private void OnGUI()
    {
        if (stopped || !panel) return;
        try { DrawPanel(); }
        catch (Exception e) { panel = false; Plugin.Logger.LogError("Pose panel disabled: " + e); RestoreCursor(); }
    }
    private void DrawPanel()
    {
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        int oldDepth = GUI.depth;
        try
        {
            float scale = Mathf.Min(Screen.height / 1020f, 1.25f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.depth = -10000;
            GUI.color = new Color(.04f, .07f, .12f, .98f);
            GUI.DrawTexture(new Rect(8, 8, 388, 1004), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle { font = GUI.skin.font, fontSize = 15, wordWrap = true };
                labelStyle.normal.textColor = Color.white;
            }
            GUI.Label(new Rect(20, 16, 365, 28), "MANAKA / DESKTOP POSE LAB", labelStyle);
            GUI.Label(new Rect(20, 44, 365, 44), $"{control.Frame.TrackingPoints}-POINT SYNTHETIC BODY TRACKING\nF6 control | F7 demo | F8 camera | F9 reset | F10 UI", labelStyle);
            if (GUI.Button(new Rect(20, 92, 176, 30), rig == null ? "ENABLE CONTROL" : "RELEASE CONTROL")) Toggle();
            if (GUI.Button(new Rect(204, 92, 176, 30), control.Demo ? "DEMO: ON" : "DEMO: OFF")) { control.Demo = !control.Demo; demoStarted = Time.unscaledTime; }
            if (GUI.Button(new Rect(20, 128, 112, 27), "Neutral")) ResetInput();
            if (GUI.Button(new Rect(140, 128, 112, 27), "Save input")) SaveInput();
            if (GUI.Button(new Rect(260, 128, 120, 27), "Preview " + (control.Preview ? "ON" : "OFF"))) control.Preview = !control.Preview;
            GUI.Label(new Rect(20, 161, 360, 46), message, labelStyle);
            var modes = new[] { 6, 8, 10, 11 };
            for (int i = 0; i < modes.Length; i++)
                if (GUI.Button(new Rect(20 + i * 92, 210, 86, 26), (control.Frame.TrackingPoints == modes[i] ? "> " : "") + modes[i] + " points"))
                { control.Frame.TrackingPoints = modes[i]; targetIndex = Math.Min(targetIndex, modes[i] - 1); }
            targetIndex = Math.Min(targetIndex, control.Frame.TrackingPoints - 1);
            for (int i = 0; i < targetNames.Length; i++)
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = TrackingLayout.IsActive(control.Frame.TrackingPoints, i);
                if (GUI.Button(new Rect(20 + (i % 4) * 92, 242 + (i / 4) * 27, 86, 24), (targetIndex == i ? "> " : "") + targetNames[i])) targetIndex = i;
                GUI.enabled = wasEnabled;
            }
            var targets = control.Frame.AllTargets();
            var target = targets[targetIndex];
            bool bendTarget = targetIndex is >= 6 and <= 9;
            GUI.Label(new Rect(20, 326, 360, 24), bendTarget ? "Joint bend target: position only (metres)" : "Tracker offsets: metres / degrees", labelStyle);
            float y = 354;
            bool changed = EditOffset(target, ref y, .6f, bendTarget);
            control.Frame.LeftGrip = Slider("Left grip", control.Frame.LeftGrip, 0, 1, ref y, ref changed);
            control.Frame.RightGrip = Slider("Right grip", control.Frame.RightGrip, 0, 1, ref y, ref changed);
            if (changed) control.Demo = false;
            bool cameraChanged = false;
            control.CameraYaw = Slider("View yaw", control.CameraYaw, -180, 180, ref y, ref cameraChanged);
            if (GUI.Button(new Rect(20, y, 360, 24), "Target markers: " + (control.ShowTargets ? "ON" : "OFF"))) control.ShowTargets = !control.ShowTargets;
            y += 30;
            GUI.Label(new Rect(20, y, 360, 24), "DIRECT BONE CONTROL (local offsets)", labelStyle); y += 27;
            if (GUI.Button(new Rect(20, y, 360, 23), "Filter: " + boneGroups[boneGroup])) { boneGroup = (boneGroup + 1) % boneGroups.Length; boneIndex = 0; }
            y += 28;
            if (rig != null)
            {
                var list = rig.Bones.Where(b => MatchesGroup(b.Transform.name)).ToArray();
                if (list.Length > 0)
                {
                    boneIndex = Math.Clamp(boneIndex, 0, list.Length - 1);
                    if (GUI.Button(new Rect(20, y, 38, 25), "<")) boneIndex = (boneIndex + list.Length - 1) % list.Length;
                    if (GUI.Button(new Rect(342, y, 38, 25), ">")) boneIndex = (boneIndex + 1) % list.Length;
                    var bone = list[boneIndex];
                    GUI.Label(new Rect(64, y, 274, 27), $"{boneIndex + 1}/{list.Length}  {bone.Transform.name}", labelStyle); y += 30;
                    if (!control.Frame.Bones.TryGetValue(bone.Path, out var value)) value = new();
                    if (EditOffset(value, ref y, .05f)) { control.Frame.Bones[bone.Path] = value; control.Demo = false; }
                }
                else { GUI.Label(new Rect(20, y, 360, 26), "No matching bones.", labelStyle); y += 26; }
                string extra = rig.AppliedTrackingPoints == 11 ? $"Chest {rig.ChestError * 100:F1} cm / {rig.ChestRotationError:F1} deg" : "Knee/elbow targets determine bend direction.";
                GUI.Label(new Rect(20, 956, 360, 48), $"Head {rig.HeadError * 100:F1} cm | Limbs {rig.MaxLimbError * 100:F1} cm\n{extra}", labelStyle);
            }
        }
        finally { GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth; }
    }
    private bool MatchesGroup(string name)
    {
        string n = name.ToLowerInvariant();
        string[] words = boneGroup switch
        {
            1 => new[] { "head", "neck", "eye", "jaw" },
            2 => new[] { "spine", "chest", "hip" },
            3 => new[] { "arm", "shoulder", "hand", "wrist" },
            4 => new[] { "thumb", "index", "middle", "ring", "little", "pinky", "finger" },
            5 => new[] { "leg", "thigh", "knee", "foot", "ankle", "toe" },
            _ => Array.Empty<string>()
        };
        return words.Length == 0 || words.Any(n.Contains);
    }
    private bool EditOffset(TargetOffset t, ref float y, float range, bool positionOnly = false)
    {
        bool changed = false;
        t.X = Slider("X / right", t.X, -range, range, ref y, ref changed);
        t.Y = Slider("Y / up", t.Y, -range, range, ref y, ref changed);
        t.Z = Slider("Z / forward", t.Z, -range, range, ref y, ref changed);
        if (positionOnly) return changed;
        t.Pitch = Slider("Pitch", t.Pitch, -90, 90, ref y, ref changed);
        t.Yaw = Slider("Yaw", t.Yaw, -90, 90, ref y, ref changed);
        t.Roll = Slider("Roll", t.Roll, -90, 90, ref y, ref changed);
        return changed;
    }
    private float Slider(string name, float value, float min, float max, ref float y, ref bool changed)
    {
        GUI.Label(new Rect(20, y, 150, 23), $"{name}  {value:F2}", labelStyle);
        float next = GUI.HorizontalSlider(new Rect(172, y + 7, 206, 17), value, min, max);
        y += 25;
        if (Mathf.Abs(next - value) > .00001f) changed = true;
        return next;
    }

    internal void Shutdown()
    {
        if (stopped) return;
        stopped = true; control.Enabled = false; Release();
        foreach (var hook in cameraHooks) if (hook != null) Object.Destroy(hook);
        foreach (var m in markers) if (m != null) Object.Destroy(m);
        if (preview != null) Object.Destroy(preview.gameObject);
        if (markerMaterial != null) Object.Destroy(markerMaterial);
        if (Instance == this) Instance = null;
    }
    private void OnDestroy() => Shutdown();
}
