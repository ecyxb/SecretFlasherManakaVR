using System.Security.Cryptography;
using System.Text;

namespace ManakaVR.Configurator;

public sealed record ConfigSnapshot(string Revision, bool Exists, List<ConfigField> Fields);
public sealed record SaveRequest(string Revision, Dictionary<string, string> Changes);
public sealed class ConfigConflictException(string message) : Exception(message);

public sealed class ConfigStore
{
    private readonly object gate = new();
    private readonly string defaultText;
    private readonly ConfigDocument defaults;
    private readonly Func<bool> isGameRunning;
    public string Path { get; }
    public string BackupDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "ManakaVRConfigBackups");

    public ConfigStore(string path, string fallback, Func<bool>? running = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        defaultText = fallback;
        defaults = new ConfigDocument(fallback);
        isGameRunning = running ?? (() => false);
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public ConfigSnapshot Read()
    {
        lock (gate)
        {
            bool exists = File.Exists(Path);
            byte[] bytes = exists ? File.ReadAllBytes(Path) : [];
            var doc = new ConfigDocument(exists ? Decode(bytes) : defaultText, defaults);
            return new(Revision(bytes), exists, doc.Fields);
        }
    }
    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

    public (ConfigSnapshot Snapshot, string? Backup) Save(SaveRequest request)
    {
        lock (gate)
        {
            if (request.Changes == null || request.Changes.Count > 1000 || request.Changes.Any(p => p.Value == null))
                throw new InvalidDataException("保存请求格式无效。");
            if (isGameRunning()) throw new ConfigConflictException("游戏正在运行。草稿已保留，请关闭游戏后再保存，避免游戏覆盖配置。");
            bool exists = File.Exists(Path);
            byte[] original = exists ? File.ReadAllBytes(Path) : [];
            if (request.Revision != Revision(original)) throw new ConfigConflictException("配置已被其他程序修改。请重新读取后合并修改，尚未覆盖文件。");
            var doc = new ConfigDocument(exists ? Decode(original) : defaultText, defaults);
            string result = doc.Apply(request.Changes);
            if (exists && result == Decode(original)) return (Read(), null);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            string temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string? backup = null;
            try
            {
                var encoding = new UTF8Encoding(original.AsSpan().StartsWith(Encoding.UTF8.Preamble));
                File.WriteAllText(temp, result, encoding);
                if (isGameRunning()) throw new ConfigConflictException("游戏刚刚启动，保存已取消。请关闭游戏后重试。");
                if (exists)
                {
                    if (Revision(File.ReadAllBytes(Path)) != request.Revision) throw new ConfigConflictException("保存前检测到外部修改，请重新读取。");
                    Directory.CreateDirectory(BackupDirectory);
                    backup = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".cfg";
                    File.Replace(temp, Path, System.IO.Path.Combine(BackupDirectory, backup));
                }
                else File.Move(temp, Path, false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return (Read(), backup);
        }
    }

    public object[] Backups() => Directory.Exists(BackupDirectory)
        ? new DirectoryInfo(BackupDirectory).GetFiles("*.cfg")
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"^\d{8}-\d{6}-\d{3}-[a-f0-9]{6}\.cfg$"))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(30)
            .Select(f => (object)new { name = f.Name,
                time = DateTime.ParseExact(f.Name[..19], "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal).ToUniversalTime(), bytes = f.Length }).ToArray() : [];

    public List<ConfigField> ReadBackup(string name)
    {
        if (System.IO.Path.GetFileName(name) != name || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{8}-\d{6}-\d{3}-[a-f0-9]{6}\.cfg$"))
            throw new InvalidDataException("备份名称无效。");
        return new ConfigDocument(File.ReadAllText(System.IO.Path.Combine(BackupDirectory, name)), defaults).Fields;
    }
}
