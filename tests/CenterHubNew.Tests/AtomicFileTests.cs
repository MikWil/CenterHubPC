using CenterHubNew.MVVM.Services;
using Xunit;

namespace CenterHubNew.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "centerhub-tests-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void WriteAllText_overwrites_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_dir, "settings.json");

        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void QuarantineCorrupt_moves_the_file_aside_with_contents_intact()
    {
        var path = Path.Combine(_dir, "notes.json");
        File.WriteAllText(path, "{ not valid json");

        var moved = AtomicFile.QuarantineCorrupt(path);

        Assert.NotNull(moved);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not valid json", File.ReadAllText(moved!));
    }

    [Fact]
    public void QuarantineCorrupt_on_missing_file_does_nothing()
        => Assert.Null(AtomicFile.QuarantineCorrupt(Path.Combine(_dir, "missing.json")));
}
