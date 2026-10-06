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
}
