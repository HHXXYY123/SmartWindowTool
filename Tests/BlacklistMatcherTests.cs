using System;
using System.Diagnostics;
using SmartWindowTool.Core;
using SmartWindowTool.Models;
using Xunit;

namespace SmartWindowTool.Tests;

public class BlacklistMatcherTests
{
    private const string JavawPath = @"C:\Program Files\Eclipse Adoptium\jdk-21.0.11.10-hotspot\bin\javaw.exe";

    [Fact]
    public void FullPathEntry_MatchesSameFullPath()
    {
        // 用户的实际场景：黑名单里存的是完整路径
        Assert.True(BlacklistMatcher.Matches("javaw.exe", JavawPath, JavawPath));
    }

    [Fact]
    public void FullPathEntry_MatchesCaseInsensitive()
    {
        Assert.True(BlacklistMatcher.Matches("javaw.exe", JavawPath.ToLowerInvariant(), JavawPath));
    }

    [Fact]
    public void FullPathEntry_DoesNotMatchDifferentDirectory()
    {
        Assert.False(BlacklistMatcher.Matches("javaw.exe", @"C:\OtherJdk\bin\javaw.exe", JavawPath));
    }

    [Fact]
    public void BareNameEntry_MatchesAnyPath()
    {
        Assert.True(BlacklistMatcher.Matches("javaw.exe", JavawPath, "javaw.exe"));
        Assert.True(BlacklistMatcher.Matches("javaw.exe", JavawPath, "javaw"));
        Assert.True(BlacklistMatcher.Matches("JAVAW.exe", JavawPath, "javaw.exe"));
    }

    [Fact]
    public void QuotedEntry_IsNormalized()
    {
        Assert.True(BlacklistMatcher.Matches("javaw.exe", JavawPath, "\"" + JavawPath + "\""));
    }

    [Fact]
    public void UnrelatedProcess_DoesNotMatch()
    {
        Assert.False(BlacklistMatcher.Matches("explorer.exe", @"C:\Windows\explorer.exe", JavawPath));
        Assert.False(BlacklistMatcher.Matches("explorer.exe", @"C:\Windows\explorer.exe", "javaw.exe"));
    }

    [Fact]
    public void NullArguments_AreSafe()
    {
        Assert.False(BlacklistMatcher.Matches(null, null, null));
        Assert.False(BlacklistMatcher.Matches(null, null, string.Empty));
        Assert.False(BlacklistMatcher.Matches("javaw.exe", null, JavawPath));
    }

    [Fact]
    public void IsBlacklisted_SkipsBlankEntries()
    {
        Assert.False(BlacklistMatcher.IsBlacklisted("javaw.exe", JavawPath, new[] { "", "  ", null! }));
        Assert.True(BlacklistMatcher.IsBlacklisted("javaw.exe", JavawPath, new[] { "notepad.exe", JavawPath }));
    }

    // --- 真实进程集成测试：确保 HookService 的进程信息采集（GetProcessById + MainModule）全链路可用 ---

    [Fact]
    public void HookService_MatchesCurrentTestProcess_ByName()
    {
        var settings = new AppSettings();
        settings.BlacklistProcesses.Add(new BlacklistEntry { Entry = Process.GetCurrentProcess().ProcessName + ".exe" });

        var service = new HookService(settings);

        Assert.True(service.IsProcessBlacklisted((uint)Process.GetCurrentProcess().Id));
    }

    [Fact]
    public void HookService_MatchesCurrentTestProcess_ByFullPath()
    {
        string? path = Process.GetCurrentProcess().MainModule?.FileName;
        Assert.False(string.IsNullOrEmpty(path));

        var settings = new AppSettings();
        settings.BlacklistProcesses.Add(new BlacklistEntry { Entry = path! });

        var service = new HookService(settings);

        Assert.True(service.IsProcessBlacklisted((uint)Process.GetCurrentProcess().Id));
    }

    [Fact]
    public void HookService_DoesNotMatch_WhenNotListed()
    {
        var settings = new AppSettings();
        settings.BlacklistProcesses.Add(new BlacklistEntry { Entry = "definitely-not-this-process.exe" });

        var service = new HookService(settings);

        Assert.False(service.IsProcessBlacklisted((uint)Process.GetCurrentProcess().Id));
    }

    [Fact]
    public void FindMatch_ReturnsConfiguredMode()
    {
        var entries = new[]
        {
            new BlacklistEntry { Entry = "other.exe", Mode = BlacklistMode.Full },
            new BlacklistEntry { Entry = JavawPath, Mode = BlacklistMode.FunctionOnly }
        };

        BlacklistEntry? match = BlacklistMatcher.FindMatch("javaw.exe", JavawPath, entries);

        Assert.NotNull(match);
        Assert.Equal(BlacklistMode.FunctionOnly, match!.Mode);
    }

    [Fact]
    public void BlacklistEntry_DefaultsToFullMode()
    {
        var entry = new BlacklistEntry { Entry = "game.exe" };

        Assert.Equal(BlacklistMode.Full, entry.Mode);
        Assert.True(entry.IsFull);
        Assert.False(entry.IsFunctionOnly);
    }
}
