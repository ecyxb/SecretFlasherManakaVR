using System.Globalization;
using System.Text.RegularExpressions;

namespace ManakaVR.Configurator;

public sealed record ConfigField(string Section, string Key, string Value)
{
    public string Id => Section + " :: " + Key;
    public string Type { get; init; } = "String";
    public string? Default { get; init; }
    public string Description { get; init; } = "";
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string[] Options { get; init; } = [];
}

public sealed class ConfigDocument
{
    private static readonly Regex Assignment = new(@"^(\s*([^#;=\[\]\r\n]+?)\s*=\s*)(.*)$");
    private static readonly Regex Range = new(@"^# Acceptable value range: From (\S+) to (\S+)$");
    public List<ConfigField> Fields { get; } = [];
    private readonly List<string> lines;
    private readonly Dictionary<string, int> locations = new(StringComparer.Ordinal);
    private readonly string newline;

    public ConfigDocument(string text, ConfigDocument? defaults = null)
    {
        newline = text.Contains("\r\n") ? "\r\n" : "\n";
        lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        string section = "";
        List<string> comments = [];
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            { section = line[1..^1].Trim(); comments.Clear(); continue; }
            if (line.StartsWith('#') || line.StartsWith(';')) { comments.Add(line); continue; }
            if (line.Length == 0) continue;
            var match = Assignment.Match(lines[i]);
            if (!match.Success || section.Length == 0) { comments.Clear(); continue; }
            var field = new ConfigField(section, match.Groups[2].Value.Trim(), match.Groups[3].Value.Trim());
            var fallback = defaults?.Fields.FirstOrDefault(x => x.Id == field.Id);
            string? Metadata(string prefix) => comments.LastOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim();
            var range = comments.Select(c => Range.Match(c)).LastOrDefault(m => m.Success);
            var descriptions = comments.Where(c => c.StartsWith("## ")).Select(c => c[3..]);
            field = field with
            {
                Type = Metadata("# Setting type:") ?? fallback?.Type ?? "String",
                Default = Metadata("# Default value:") ?? fallback?.Default,
                Description = descriptions.Any() ? string.Join("\n", descriptions) : fallback?.Description ?? "",
                Options = Metadata("# Acceptable values:")?.Split(',', StringSplitOptions.TrimEntries) ?? fallback?.Options ?? [],
                Min = range != null ? double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture) : fallback?.Min,
                Max = range != null ? double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture) : fallback?.Max
            };
            if (!locations.TryAdd(field.Id, i)) throw new InvalidDataException($"配置项重复：{field.Section} / {field.Key}。请先修正配置文件。");
            Fields.Add(field);
            comments.Clear();
        }
        if (defaults != null)
            foreach (var field in defaults.Fields.Where(f => !locations.ContainsKey(f.Id))) Fields.Add(field);
    }

    public static string Validate(ConfigField field, string value)
    {
        if (value.Length > 16384 || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidDataException($"{field.Key}：内容过长或包含换行。");
        value = value.Trim();
        if (field.Type == "Boolean")
        {
            if (!bool.TryParse(value, out bool boolean)) throw new InvalidDataException($"{field.Key}：需要开关值。");
            return boolean ? "true" : "false";
        }
        if (field.Options.Length > 0 && !field.Options.Contains(value, StringComparer.Ordinal))
            throw new InvalidDataException($"{field.Key}：请选择列表中的有效选项。");
        if (field.Type is "Single" or "Double" or "Int32")
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw new InvalidDataException($"{field.Key}：请输入有效数字。");
            if (field.Type == "Int32" && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                throw new InvalidDataException($"{field.Key}：请输入整数。");
            if (field.Min.HasValue && number < field.Min || field.Max.HasValue && number > field.Max)
                throw new InvalidDataException($"{field.Key}：允许范围为 {field.Min}–{field.Max}。");
        }
        return value;
    }

    public string Apply(IReadOnlyDictionary<string, string> changes)
    {
        var values = Fields.ToDictionary(f => f.Id, f => f.Value);
        foreach (var (id, value) in changes)
        {
            var field = Fields.FirstOrDefault(f => f.Id == id) ?? throw new InvalidDataException("配置项不存在，请重新读取。");
            values[id] = Validate(field, value);
        }
        // Range pairs must not be inverted, including when only one side changes.
        foreach (var field in Fields.Where(f => f.Key.Contains("Min", StringComparison.Ordinal)))
        {
            var other = Fields.FirstOrDefault(f => f.Section == field.Section && f.Key == field.Key.Replace("Min", "Max", StringComparison.Ordinal));
            if (other == null || (!changes.ContainsKey(field.Id) && !changes.ContainsKey(other.Id))) continue;
            if (double.TryParse(values[field.Id], CultureInfo.InvariantCulture, out var min) &&
                double.TryParse(values[other.Id], CultureInfo.InvariantCulture, out var max) && min > max)
                throw new InvalidDataException($"{field.Key} 不能大于 {other.Key}。");
        }
        var output = new List<string>(lines);
        foreach (var (id, _) in changes)
        {
            var field = Fields.First(f => f.Id == id);
            if (locations.TryGetValue(id, out var index))
                output[index] = Assignment.Match(output[index]).Groups[1].Value + values[id];
        }
        // Insert missing defaults into their section without duplicating section headers.
        foreach (var group in changes.Keys.Where(id => !locations.ContainsKey(id)).Select(id => Fields.First(f => f.Id == id)).GroupBy(f => f.Section))
        {
            var sectionLine = output.FindIndex(l => l.Trim() == "[" + group.Key + "]");
            int insertAt;
            if (sectionLine < 0) { output.Add(""); output.Add("[" + group.Key + "]"); insertAt = output.Count; }
            else
            {
                insertAt = output.FindIndex(sectionLine + 1, l => l.TrimStart().StartsWith('['));
                if (insertAt < 0) insertAt = output.Count;
            }
            foreach (var field in group)
            {
                output.InsertRange(insertAt, ["", "# Setting type: " + field.Type,
                    "# Default value: " + field.Default, field.Key + " = " + values[field.Id]]);
                insertAt += 4;
            }
        }
        return string.Join(newline, output);
    }
}
