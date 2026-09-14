using System.Text;
using ManakaVR.Configurator;

int count = 0;
void Check(bool result, string why) { count++; if (!result) throw new Exception(why); }
void Reject(Action action, string why) { try { action(); } catch (Exception e) when (e is InvalidDataException or ConfigConflictException) { count++; return; } throw new Exception(why); }
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string fallback = File.ReadAllText(Path.Combine(repo, "src/ManakaVR.Configurator/Data/defaults.cfg"));
var defaults = new ConfigDocument(fallback);
Check(defaults.Fields.Count == 95, "All 95 editable config entries must be present, including five independent profiles.");
Check(defaults.Fields.Select(f => f.Id).Distinct().Count() == 95, "Repeated profile keys need section-qualified IDs.");
foreach (var f in defaults.Fields)
{
    Check(f.Default != null, "Every known setting needs a default: " + f.Key);
    Check(ConfigDocument.Validate(f, f.Default!) == f.Default, "Generated defaults must pass their own ranges and enum validation.");
}
var folder = Path.Combine(Path.GetTempPath(), "manaka-config-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "vr.cfg");
    bool running = false;
    var store = new ConfigStore(path, fallback, () => running);
    var fresh = store.Read();
    Check(!fresh.Exists && fresh.Fields.Count == 95, "No-game-first-run must expose the full default config.");
    store.Save(new(fresh.Revision, []));
    Check(File.Exists(path), "Saving a new configuration requires no prior game launch.");
    var original = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n[Future Section]\r\n# Keep me\r\nNewOption = hello=world\r\n";
    File.WriteAllText(path, original, new UTF8Encoding(true));
    var beforeBytes = File.ReadAllBytes(path);
    var snapshot = store.Read();
    var three = snapshot.Fields.Single(f => f.Key == "TrackingScale" && f.Section.StartsWith("08"));
    var six = snapshot.Fields.Single(f => f.Key == "TrackingScale" && f.Section.StartsWith("09"));
    var saved = store.Save(new(snapshot.Revision, new() { [three.Id] = "1.125" }));
    Check(saved.Snapshot.Fields.Single(f => f.Id == three.Id).Value == "1.125", "Target profile value saved.");
    Check(saved.Snapshot.Fields.Single(f => f.Id == six.Id).Value == six.Value, "Saving three points must not alter the six-point profile.");
    Check(File.ReadAllBytes(Path.Combine(store.BackupDirectory, saved.Backup!)).SequenceEqual(beforeBytes), "Backup must preserve exact previous bytes, including BOM and comments.");
    var afterText = File.ReadAllText(path);
    Check(afterText.Contains("# Keep me\r\nNewOption = hello=world"), "Unknown sections and values containing '=' must be preserved.");
    Check(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble), "Preserve BOM.");
    Check(afterText.Count(c => c == '\n') == afterText.Count(c => c == '\r'), "Preserve Windows newlines.");
    Check(store.ReadBackup(saved.Backup!).Single(f => f.Id == three.Id).Value == three.Value, "Backup can be loaded as a draft.");
    Reject(() => store.ReadBackup("../vr.cfg"), "Backup traversal rejected.");
    Reject(() => store.Save(new(snapshot.Revision, new() { [three.Id] = "1.2" })), "Stale version cannot overwrite external changes.");
    var current = store.Read();
    foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity", "0.1", "2", "1\n[Injected]", "not-a-number" })
        Reject(() => store.Save(new(current.Revision, new() { [three.Id] = invalid })), "Invalid numeric value rejected: " + invalid);
    var mode = current.Fields.Single(f => f.Key == "TrackingMode");
    Reject(() => store.Save(new(current.Revision, new() { [mode.Id] = "TwelvePoint" })), "Unknown enum rejected.");
    Reject(() => store.Save(new(current.Revision, new() { ["unknown"] = "true" })), "Unknown field rejected.");
    var minimum = current.Fields.Single(f => f.Key == "HeadPositionCameraOffsetMinY");
    Reject(() => store.Save(new(current.Revision, new() { [minimum.Id] = "3" })), "Inverted min/max pair rejected.");
    var mirrorCount = current.Fields.Single(f => f.Key == "VrMirrorMaxUpdatesPerFrame");
    Reject(() => store.Save(new(current.Revision, new() { [mirrorCount.Id] = "1.5" })), "Fractional integer rejected.");
    var toggle = current.Fields.Single(f => f.Key == "EnableVR");
    Reject(() => store.Save(new(current.Revision, new() { [toggle.Id] = "sometimes" })), "Invalid boolean rejected.");
    Check(store.Read().Revision == current.Revision, "Validation failures must never mutate the config.");
    running = true;
    Reject(() => store.Save(new(current.Revision, new() { [three.Id] = "1.2" })), "Game-running guard prevents lost settings.");
    running = false;
    File.AppendAllText(path, "# External edit\r\n");
    Reject(() => store.Save(new(current.Revision, new() { [three.Id] = "1.2" })), "External-file edits detected by revision hash.");
    var missingText = "[08 Three Point - 三点追踪]\r\n# Existing user setting\r\nAutoScale = false\r\n";
    var partial = new ConfigDocument(missingText, defaults);
    var inserted = partial.Apply(new Dictionary<string,string> { [three.Id] = "1.2" });
    Check(inserted.Split("[08 Three Point - 三点追踪]").Length == 2, "Adding a missing key must not duplicate a section.");
    Check(inserted.Contains("AutoScale = false") && inserted.Contains("TrackingScale = 1.2"), "Partial configs merge defaults without changing existing values.");
    Reject(() => new ConfigDocument("[A]\nX = 1\nX = 2"), "Duplicate keys cannot cause silent ambiguous writes.");
    Check(new ConfigDocument("[A]\nX = 1\n[B]\nX = 2").Fields.Count == 2, "Same key in different sections is valid.");
    var noOp = store.Save(new(store.Read().Revision, []));
    Check(noOp.Backup == null, "A no-op save must not rewrite the config or create a backup.");
}
finally
{
    var resolved = Path.GetFullPath(folder);
    var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("manaka-config-tests-"))
        throw new Exception("Unexpected test cleanup path.");
    Directory.Delete(resolved, true);
}
Console.WriteLine($"PASS: {count} assertions (all fields, profile isolation, validation, atomic writes, backups, conflicts and first run).");
