using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CenterHubNew.MVVM.Navigation;
using CenterHubNew.MVVM.ViewModel;
using Xunit;

namespace CenterHubNew.Tests;

public class CommandPaletteTests
{
    private static PaletteCommand Cmd(string id, string title, string category = "App", string keywords = "") =>
        new(id, title, category, "", keywords, null, () => Task.CompletedTask);

    private static List<PaletteCommand> Sample() => new()
    {
        Cmd("page:home", "Go to Home", "Pages", "start overview"),
        Cmd("page:sound", "Go to Sound", "Pages", "audio voicemeeter banana"),
        Cmd("page:metronome", "Go to Metronome", "Pages", "drums beat tempo bpm"),
        Cmd("audio:mic", "Mute / unmute microphone", "Audio", "mic mute"),
        Cmd("audio:direct", "Direct mode (no Banana)", "Audio", "headset bypass"),
        Cmd("metro:toggle", "Start / stop metronome", "Metronome", "play drums"),
        Cmd("metro:tap", "Tap tempo", "Metronome", "bpm beat"),
        Cmd("app:zoomin", "Zoom in", "App", "bigger"),
    };

    private static string[] Ids(IEnumerable<PaletteCommand> commands) => commands.Select(c => c.Id).ToArray();

    // ---------- Filter: word matching ----------

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var result = CommandPaletteViewModel.Filter(Sample(), "ZOOM");
        Assert.Equal(new[] { "app:zoomin" }, Ids(result));
    }

    [Fact]
    public void Filter_EveryWordMustMatch()
    {
        var result = CommandPaletteViewModel.Filter(Sample(), "go sound");
        Assert.Equal(new[] { "page:sound" }, Ids(result));
    }

    [Fact]
    public void Filter_MatchesCategoryAndKeywords()
    {
        // "banana" is in a keyword of the Sound page and in the title of Direct mode.
        var banana = Ids(CommandPaletteViewModel.Filter(Sample(), "banana"));
        Assert.Contains("page:sound", banana);
        Assert.Contains("audio:direct", banana);

        // "audio" is a category.
        var audio = Ids(CommandPaletteViewModel.Filter(Sample(), "audio mic"));
        Assert.Equal(new[] { "audio:mic" }, audio);
    }

    [Fact]
    public void Filter_NoMatch_IsEmpty()
    {
        Assert.Empty(CommandPaletteViewModel.Filter(Sample(), "xyzzy"));
    }

    // ---------- Filter: ranking ----------

    [Fact]
    public void Filter_TitleStartsWithQuery_RanksFirst()
    {
        var commands = new List<PaletteCommand>
        {
            Cmd("a", "Open the mixer", "App", "tap"),       // keyword only
            Cmd("b", "Quick tap helper", "App"),              // title word starts with "tap"
            Cmd("c", "Tap tempo", "Metronome"),               // title starts with "tap"
        };

        Assert.Equal(new[] { "c", "b", "a" }, Ids(CommandPaletteViewModel.Filter(commands, "tap")));
    }

    [Fact]
    public void Filter_TitleWordPrefix_BeatsSubstring()
    {
        var commands = new List<PaletteCommand>
        {
            Cmd("a", "Restart Voicemeeter", "Audio"),         // "start" only inside "Restart"
            Cmd("b", "Go to Start page", "App"),              // a title word starts with "start"
        };

        Assert.Equal(new[] { "b", "a" }, Ids(CommandPaletteViewModel.Filter(commands, "start")));
    }

    [Fact]
    public void Filter_TiesKeepTheInputOrder()
    {
        var commands = new List<PaletteCommand>
        {
            Cmd("1", "Alpha tool", "App"),
            Cmd("2", "Beta tool", "App"),
            Cmd("3", "Gamma tool", "App"),
        };

        Assert.Equal(new[] { "1", "2", "3" }, Ids(CommandPaletteViewModel.Filter(commands, "tool")));
    }

    // ---------- Filter: empty query ----------

    [Fact]
    public void Filter_EmptyQuery_RecentsThenPages()
    {
        var recent = new[] { "metro:tap", "page:sound" };
        var result = Ids(CommandPaletteViewModel.Filter(Sample(), "", recent));

        Assert.Equal(new[] { "metro:tap", "page:sound", "page:home", "page:metronome" }, result);
    }

    [Fact]
    public void Filter_EmptyQuery_AtMostFiveRecents_IgnoresUnknownIds()
    {
        var commands = Enumerable.Range(0, 8).Select(i => Cmd("c" + i, "Command " + i)).ToList();
        commands.Add(Cmd("page:x", "Go to X", "Pages"));
        var recent = new[] { "gone", "c7", "c6", "c5", "c4", "c3", "c2" };

        var result = Ids(CommandPaletteViewModel.Filter(commands, "   ", recent));

        Assert.Equal(new[] { "c7", "c6", "c5", "c4", "c3", "page:x" }, result);
    }

    [Fact]
    public void Filter_EmptyQuery_NoRecents_IsJustPages()
    {
        var result = CommandPaletteViewModel.Filter(Sample(), null);
        Assert.All(result, c => Assert.Equal("Pages", c.Category));
        Assert.Equal(3, result.Count);
    }

    // ---------- Tempo parsing ----------

    [Theory]
    [InlineData("120", 120)]
    [InlineData("bpm 95", 95)]
    [InlineData("tempo 140", 140)]
    [InlineData("100bpm", 100)]
    [InlineData("  30 ", 30)]
    [InlineData("280", 280)]
    public void ParseTempo_FindsStandaloneNumbers(string query, int expected)
    {
        Assert.Equal(expected, CommandPaletteViewModel.ParseTempo(query));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("29")]
    [InlineData("281")]
    [InlineData("8")]
    [InlineData("rock 8ths")]
    [InlineData("12abc")]
    [InlineData("bpm")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseTempo_IgnoresEverythingElse(string? query)
    {
        Assert.Null(CommandPaletteViewModel.ParseTempo(query));
    }

    // ---------- The command list ----------

    [Fact]
    public void BuildCommands_ContainsEveryPage()
    {
        using var vm = new CommandPaletteViewModel();
        var commands = vm.BuildCommands();

        foreach (var page in PageRegistry.All)
        {
            var cmd = commands.SingleOrDefault(c => c.Id == "page:" + page.Key);
            Assert.NotNull(cmd);
            Assert.Equal("Go to " + page.Title, cmd!.Title);
            Assert.Equal("Pages", cmd.Category);
        }
    }

    [Fact]
    public void BuildCommands_IdsAreUnique_AndHaveTheFixedActions()
    {
        using var vm = new CommandPaletteViewModel();
        var commands = vm.BuildCommands();

        Assert.Equal(commands.Count, commands.Select(c => c.Id).Distinct().Count());
        foreach (var id in new[] { "audio:direct", "audio:restart", "audio:resync", "audio:setup", "audio:mic",
                                   "metro:toggle", "metro:tap", "metro:fill", "metro:nextpart", "metro:nextsong", "metro:prevsong",
                                   "app:zoomin", "app:zoomout", "app:zoomreset", "app:sidebar", "app:settings", "app:compact", "app:strip" })
            Assert.Contains(commands, c => c.Id == id);
    }

    [Fact]
    public void PageCommands_AreFoundByTheirTitleAndKeywords()
    {
        using var vm = new CommandPaletteViewModel();
        var commands = vm.BuildCommands();

        Assert.Contains("page:metronome", Ids(CommandPaletteViewModel.Filter(commands, "metronome")));
        Assert.Contains("page:network", Ids(CommandPaletteViewModel.Filter(commands, "wifi")));
    }
}
