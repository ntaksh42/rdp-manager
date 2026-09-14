using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using RdpManager.Services;
using Xunit;

namespace RdpManager.Tests;

/// <summary>
/// 書き出される rdp-notify.ps1 の構造テスト。
/// スクリプトは C# の文字列定数なので実行テストは難しいが、
/// 壊れると通知が黙って死ぬ性質の制約（PowerShell 5.1 互換・ヒアストリング終端・exit code）は
/// 文字列として機械的に検証できる。
/// </summary>
public class RemoteNotifyScriptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _script;

    public RemoteNotifyScriptTests()
    {
        Directory.CreateDirectory(_dir);
        var files = RemoteNotifyScript.Export(_dir);
        _script = File.ReadAllText(files[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Export_CreatesBothFiles()
    {
        Assert.True(File.Exists(Path.Combine(_dir, RemoteNotifyScript.ScriptFileName)));
        Assert.True(File.Exists(Path.Combine(_dir, RemoteNotifyScript.HooksSampleFileName)));
    }

    [Fact]
    public void Export_ScriptHasUtf8Bom()
    {
        // Windows PowerShell 5.1 は BOM が無いと非 ASCII を化けさせる。
        var bytes = File.ReadAllBytes(Path.Combine(_dir, RemoteNotifyScript.ScriptFileName));

        Assert.True(bytes.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void Script_DoesNotGuardOnSessionName()
    {
        // SESSIONNAME は昇格プロセス等で未設定になり誤検知するため、判定に使わないこと。
        // 表示（-Diag）での参照は許容するので、条件式での使用のみを禁じる。
        Assert.DoesNotContain("if ($env:SESSIONNAME", _script);
    }

    [Fact]
    public void Script_DetectsChannelByOpenResult()
    {
        Assert.Contains("WTSVirtualChannelOpen", _script);
        Assert.Contains("GetLastWin32Error", _script);
    }

    [Fact]
    public void Script_ChecksWriteResultAndWrittenLength()
    {
        // 戻り値と実書き込みバイト数の両方を検証しないと、失敗が成功に見える。
        Assert.Contains("$wrote = ", _script);
        Assert.Contains("$written -ne $bytes.Length", _script);
    }

    [Fact]
    public void Script_HereStringTerminatorIsAtColumnZero()
    {
        // PowerShell の閉じヒアストリング "@ は行頭必須。インデントすると構文エラーになる。
        var lines = _script.Split('\n');
        var terminators = lines.Where(l => l.TrimStart().StartsWith("\"@")).ToList();

        Assert.NotEmpty(terminators);
        Assert.All(terminators, l => Assert.StartsWith("\"@", l));
    }

    [Fact]
    public void Script_ExitsNonZeroOnlyUnderStrict()
    {
        // hook を壊さないため、既定では失敗しても exit 0 を保つ。
        Assert.Contains("if ($Strict -and -not $ok) { exit 1 }", _script);
        Assert.Single(Regex.Matches(_script, @"exit 1"));
    }

    [Theory]
    // PowerShell 5.1 に存在しない構文。使うとリモート側で構文エラーになる。
    [InlineData("$PSStyle")]
    [InlineData("`u{")]
    [InlineData("??")]
    [InlineData("?.")]
    public void Script_AvoidsPowerShell7OnlySyntax(string forbidden)
    {
        Assert.DoesNotContain(forbidden, _script);
    }

    [Fact]
    public void Script_ParsesUnderWindowsPowerShell51()
    {
        var path = Path.Combine(_dir, RemoteNotifyScript.ScriptFileName);
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "$e = $null; " +
            $"[void][System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$e); " +
            "if ($e -and $e.Count -gt 0) { $e | ForEach-Object { $_.Message }; exit 1 } else { exit 0 }");

        using var p = Process.Start(psi);
        if (p is null) return; // Windows PowerShell が無い環境ではスキップ
        string stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30000);

        Assert.True(p.ExitCode == 0, $"PowerShell 5.1 syntax errors:\n{stdout}");
    }
}
