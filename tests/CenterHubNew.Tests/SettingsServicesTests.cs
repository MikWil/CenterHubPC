using System.IO.Compression;
using CenterHubNew.MVVM.Services;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CenterHubNew.Tests;

public class StartupServiceTests : IDisposable
{
    private const string TestRoot = @"Software\CenterHubTests";
    private readonly string _key = TestRoot + @"\Startup-" + Guid.NewGuid().ToString("N");
    private readonly string _exe = Path.Combine(Path.GetTempPath(), "CenterHubTests", "CenterHubNew.exe");

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false); } catch { }
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(TestRoot);
            var empty = root != null && root.SubKeyCount == 0 && root.ValueCount == 0;
            root?.Dispose();
            if (empty) Registry.CurrentUser.DeleteSubKey(TestRoot, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private object? RawValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        return key?.GetValue(StartupService.ValueName);
    }

    private void SetRaw(string data)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_key);
        key.SetValue(StartupService.ValueName, data, RegistryValueKind.String);
    }

    [Fact]
    public void Disabled_when_nothing_is_registered()
    {
        var service = new StartupService(null, _key, _exe);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void Enable_writes_the_quoted_exe_path_and_disable_removes_it()
    {
        var service = new StartupService(null, _key, _exe);

        Assert.True(service.SetEnabled(true));
        Assert.True(service.IsEnabled);
        Assert.Equal("\"" + _exe + "\"", RawValue());

        Assert.True(service.SetEnabled(false));
        Assert.False(service.IsEnabled);
        Assert.Null(RawValue());
    }

    [Fact]
    public void Disabling_when_already_off_is_fine()
    {
        var service = new StartupService(null, _key, _exe);
        Assert.True(service.SetEnabled(false));
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public void A_stale_path_reads_as_off_and_enabling_overwrites_it()
    {
        SetRaw("\"C:\\Old Install\\CenterHub\\CenterHubNew.exe\"");
        var service = new StartupService(null, _key, _exe);

        Assert.False(service.IsEnabled);

        Assert.True(service.SetEnabled(true));
        Assert.True(service.IsEnabled);
        Assert.Equal("\"" + _exe + "\"", RawValue());
    }

    [Fact]
    public void The_path_comparison_ignores_case_and_quotes()
    {
        SetRaw(_exe.ToUpperInvariant());
        Assert.True(new StartupService(null, _key, _exe).IsEnabled);
    }
}

public class SettingsBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CenterHubBackup-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void MakeZip(string path, params (string Name, string Content)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static List<string> EntryNames(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Backup_holds_only_the_top_level_json_files_plus_an_info_file()
    {
        var data = Folder("data");
        File.WriteAllText(Path.Combine(data, "ui.json"), "{\"zoom\":1.2}");
        File.WriteAllText(Path.Combine(data, "quick-notes.json"), "[]");
        File.WriteAllText(Path.Combine(data, "readme.txt"), "not settings");
        File.WriteAllText(Path.Combine(data, "ui.json.corrupt-20260101000000"), "junk");
        Directory.CreateDirectory(Path.Combine(data, "sub"));
        File.WriteAllText(Path.Combine(data, "sub", "inner.json"), "{}");
        Directory.CreateDirectory(Path.Combine(data, "backups"));
        File.WriteAllText(Path.Combine(data, "backups", "old.json"), "{}");

        var zipPath = Path.Combine(Folder("out"), "b.zip");
        var count = new SettingsBackupService(null, data).CreateBackup(zipPath);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "backup-info.json", "quick-notes.json", "ui.json" }, EntryNames(zipPath));

        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("backup-info.json")!.Open());
        var info = JObject.Parse(reader.ReadToEnd());
        Assert.Equal("CenterHub", (string?)info["app"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)info["version"]));
        Assert.NotNull(info["created"]);
    }

    [Fact]
    public void Backup_of_an_empty_folder_still_makes_a_valid_zip()
    {
        var zipPath = Path.Combine(Folder("out"), "b.zip");
        var count = new SettingsBackupService(null, Path.Combine(_root, "missing")).CreateBackup(zipPath);

        Assert.Equal(0, count);
        Assert.Equal(new[] { "backup-info.json" }, EntryNames(zipPath));
    }

    [Fact]
    public void Backup_replaces_an_existing_file()
    {
        var data = Folder("data");
        File.WriteAllText(Path.Combine(data, "ui.json"), "{}");
        var zipPath = Path.Combine(Folder("out"), "b.zip");
        File.WriteAllText(zipPath, "old junk");

        new SettingsBackupService(null, data).CreateBackup(zipPath);

        Assert.Contains("ui.json", EntryNames(zipPath));
        Assert.False(File.Exists(zipPath + ".tmp"));
    }

    [Fact]
    public void Round_trip_restores_files_and_saves_the_previous_ones_first()
    {
        var source = Folder("source");
        File.WriteAllText(Path.Combine(source, "ui.json"), "{\"zoom\":1.3}");
        File.WriteAllText(Path.Combine(source, "hotkeys.json"), "{\"a\":1}");
        var zipPath = Path.Combine(Folder("out"), "b.zip");
        new SettingsBackupService(null, source).CreateBackup(zipPath);

        var target = Folder("target");
        File.WriteAllText(Path.Combine(target, "ui.json"), "{\"zoom\":0.9}");
        File.WriteAllText(Path.Combine(target, "other.json"), "{\"keep\":true}");
        File.WriteAllText(Path.Combine(target, "notes.txt"), "keep me too");

        var result = new SettingsBackupService(null, target).Restore(zipPath);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.FilesRestored);
        Assert.Equal("{\"zoom\":1.3}", File.ReadAllText(Path.Combine(target, "ui.json")));
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(target, "hotkeys.json")));

        // files that are not in the backup stay as they were
        Assert.Equal("{\"keep\":true}", File.ReadAllText(Path.Combine(target, "other.json")));
        Assert.Equal("keep me too", File.ReadAllText(Path.Combine(target, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(target, "backup-info.json")));

        // the previous settings were saved before anything was written
        Assert.NotNull(result.SafetyBackupPath);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal(Path.Combine(target, "backups"), Path.GetDirectoryName(result.SafetyBackupPath));
        Assert.Matches(@"^before-restore-\d{8}-\d{6}\.zip$", Path.GetFileName(result.SafetyBackupPath!));

        using var safety = ZipFile.OpenRead(result.SafetyBackupPath!);
        using var reader = new StreamReader(safety.GetEntry("ui.json")!.Open());
        Assert.Equal("{\"zoom\":0.9}", reader.ReadToEnd());
    }

    public static IEnumerable<object[]> BadBackups()
    {
        yield return new object[] { "path traversal", new[] { ("ui.json", "{}"), ("../evil.json", "{}") } };
        yield return new object[] { "backslash traversal", new[] { ("..\\evil.json", "{}") } };
        yield return new object[] { "subfolder", new[] { ("sub/inner.json", "{}") } };
        yield return new object[] { "absolute path", new[] { ("C:/evil.json", "{}") } };
        yield return new object[] { "non-json file", new[] { ("ui.json", "{}"), ("evil.exe", "MZ") } };
        yield return new object[] { "invalid json", new[] { ("ui.json", "{}"), ("hotkeys.json", "{ not json") } };
        yield return new object[] { "empty json file", new[] { ("hotkeys.json", "") } };
        yield return new object[] { "only the info file", new[] { ("backup-info.json", "{}") } };
    }

    [Theory]
    [MemberData(nameof(BadBackups))]
    public void Bad_backups_are_refused_without_changing_anything(string why, (string Name, string Content)[] entries)
    {
        _ = why;
        var target = Folder("target");
        File.WriteAllText(Path.Combine(target, "ui.json"), "{\"zoom\":0.9}");
        var zipPath = Path.Combine(Folder("out"), "bad.zip");
        MakeZip(zipPath, entries);

        var result = new SettingsBackupService(null, target).Restore(zipPath);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(0, result.FilesRestored);
        Assert.Equal("{\"zoom\":0.9}", File.ReadAllText(Path.Combine(target, "ui.json")));
        Assert.Equal(new[] { Path.Combine(target, "ui.json") }, Directory.GetFiles(target));
        Assert.Empty(Directory.GetDirectories(target)); // no before-restore backup for a refused restore
        Assert.False(File.Exists(Path.Combine(_root, "evil.json")));
        Assert.False(File.Exists(Path.Combine(_root, "out", "evil.json")));
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_refused()
    {
        var target = Folder("target");
        File.WriteAllText(Path.Combine(target, "ui.json"), "{}");
        var notZip = Path.Combine(Folder("out"), "fake.zip");
        File.WriteAllText(notZip, "this is not a zip");

        var result = new SettingsBackupService(null, target).Restore(notZip);

        Assert.False(result.Success);
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "ui.json")));
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        var result = new SettingsBackupService(null, Folder("target")).Restore(Path.Combine(_root, "nope.zip"));
        Assert.False(result.Success);
    }

    [Fact]
    public void Restore_works_into_a_folder_that_has_no_settings_yet()
    {
        var source = Folder("source");
        File.WriteAllText(Path.Combine(source, "ui.json"), "{\"zoom\":1.1}");
        var zipPath = Path.Combine(Folder("out"), "b.zip");
        new SettingsBackupService(null, source).CreateBackup(zipPath);

        var target = Path.Combine(_root, "fresh");
        var result = new SettingsBackupService(null, target).Restore(zipPath);

        Assert.True(result.Success, result.Message);
        Assert.Equal("{\"zoom\":1.1}", File.ReadAllText(Path.Combine(target, "ui.json")));
    }

    [Theory]
    [InlineData("ui.json", true)]
    [InlineData("UI.JSON", true)]
    [InlineData("my settings.json", true)]
    [InlineData(".json", false)]
    [InlineData("ui.txt", false)]
    [InlineData("ui.json.exe", false)]
    [InlineData("../ui.json", false)]
    [InlineData("a/b.json", false)]
    [InlineData("a\\b.json", false)]
    [InlineData("C:\\ui.json", false)]
    [InlineData("ui..json", false)]
    [InlineData("", false)]
    public void File_name_rules(string name, bool ok)
    {
        Assert.Equal(ok, SettingsBackupService.IsPlainJsonFileName(name));
    }
}
