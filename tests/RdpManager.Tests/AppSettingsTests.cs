using System.IO;
using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rdpmanager-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "appsettings.json");

    public AppSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveNamedLayout_SameNameOverwritesInPlace()
    {
        var s = new AppSettings();
        s.SaveNamedLayout("Work", "H0.5(P,P)", new() { new() { "a" }, new() { "b" } });
        s.SaveNamedLayout("Home", "P", new() { new() { "c" } });
        s.SaveNamedLayout("work", "P", new() { new() { "d" } });

        Assert.Equal(2, s.SavedLayouts.Count);
        Assert.Equal("work", s.SavedLayouts[0].Name);
        Assert.Equal("P", s.SavedLayouts[0].Layout);
    }

    [Fact]
    public void DeleteNamedLayout_RemovesByNameIgnoringCase()
    {
        var s = new AppSettings();
        s.SaveNamedLayout("Work", "P", new() { new() { "a" } });

        Assert.True(s.DeleteNamedLayout("WORK"));
        Assert.False(s.DeleteNamedLayout("Work"));
        Assert.Empty(s.SavedLayouts);
    }

    [Fact]
    public void RemoveOpenSessions_AlsoPrunesSavedLayouts()
    {
        var s = new AppSettings();
        s.SaveNamedLayout("Work", "H0.5(P,P)", new() { new() { "a", "b" }, new() { "c" } });

        Assert.True(s.RemoveOpenSessions(new HashSet<string> { "b" }));
        Assert.Equal(new[] { "a" }, s.SavedLayouts[0].Panes[0]);
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefaults()
    {
        var s = AppSettings.LoadFrom(SettingsPath);

        Assert.True(s.RestoreSessions);
    }

    [Fact]
    public void LoadFrom_CorruptFile_BacksUpAndRestoresFromPrev()
    {
        File.WriteAllText(SettingsPath, "{ broken");
        File.WriteAllText(SettingsPath + ".prev", "{\"DarkMode\":true,\"QuickSwitchKey\":65}");

        var s = AppSettings.LoadFrom(SettingsPath);

        Assert.True(s.DarkMode);
        Assert.Equal(65u, s.QuickSwitchKey);
        Assert.Equal("{ broken", File.ReadAllText(SettingsPath + ".bak"));
    }

    [Fact]
    public void LoadFrom_CorruptFileWithoutPrev_ReturnsDefaultsAndKeepsBackup()
    {
        File.WriteAllText(SettingsPath, "{ broken");

        var s = AppSettings.LoadFrom(SettingsPath);

        Assert.False(s.DarkMode);
        Assert.True(File.Exists(SettingsPath + ".bak"));
    }

    [Fact]
    public void SessionsToRestore_LegacyRightPane_BecomesLeftRightSplit()
    {
        var s = AppSettings.LoadFrom(SettingsPath);
        s.OpenOnExit = new() { "a" };
        s.OpenOnExitRight = new() { "b", "c" };

        var (layout, panes) = s.SessionsToRestore();

        Assert.Equal("H0.5(P,P)", layout);
        Assert.Equal(new[] { "a" }, panes[0]);
        Assert.Equal(new[] { "b", "c" }, panes[1]);
    }

    [Fact]
    public void SaveOpenSessions_ClearsLegacyLists_AndRemoveOpenSessionsPrunesPanes()
    {
        var s = AppSettings.LoadFrom(SettingsPath);
        s.OpenOnExit = new() { "old" };
        s.SaveOpenSessions("V0.5(P,P)", new() { new() { "a", "b" }, new() { "c" } });

        Assert.Empty(s.OpenOnExit);
        Assert.Equal("V0.5(P,P)", s.SessionsToRestore().Layout);
        Assert.True(s.RemoveOpenSessions(new HashSet<string> { "b", "c" }));
        Assert.Equal(new[] { "a" }, s.OpenOnExitPanes[0]);
        Assert.Empty(s.OpenOnExitPanes[1]);
        Assert.False(s.RemoveOpenSessions(new HashSet<string> { "zzz" }));
    }
}
