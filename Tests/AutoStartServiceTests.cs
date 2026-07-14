using SmartWindowTool.Core;
using Xunit;

namespace SmartWindowTool.Tests;

public class AutoStartServiceTests
{
    [Theory]
    [InlineData("enable", true)]
    [InlineData("ENABLE", true)]
    [InlineData("disable", false)]
    public void IsHelperRequest_ParsesValidRequest(string operation, bool expectedEnable)
    {
        string[] args = { AutoStartService.HelperArgument, operation, @"DESKTOP\User", "S-1-5-21-123", "highest" };

        bool parsed = AutoStartService.IsHelperRequest(
            args,
            out bool enable,
            out bool runAsAdmin,
            out string userName,
            out string userSid);

        Assert.True(parsed);
        Assert.Equal(expectedEnable, enable);
        Assert.True(runAsAdmin);
        Assert.Equal(@"DESKTOP\User", userName);
        Assert.Equal("S-1-5-21-123", userSid);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void IsHelperRequest_RejectsUnknownOperation(string operation)
    {
        string[] args = { AutoStartService.HelperArgument, operation, @"DESKTOP\User", "S-1-5-21-123", "limited" };

        Assert.False(AutoStartService.IsHelperRequest(args, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData("highest", true)]
    [InlineData("limited", false)]
    public void IsHelperRequest_ParsesRunLevel(string runLevel, bool expectedRunAsAdmin)
    {
        string[] args = { AutoStartService.HelperArgument, "enable", @"DESKTOP\User", "S-1-5-21-123", runLevel };

        bool parsed = AutoStartService.IsHelperRequest(args, out _, out bool runAsAdmin, out _, out _);

        Assert.True(parsed);
        Assert.Equal(expectedRunAsAdmin, runAsAdmin);
    }

    [Fact]
    public void IsHelperRequest_RejectsUnknownRunLevel()
    {
        string[] args = { AutoStartService.HelperArgument, "enable", @"DESKTOP\User", "S-1-5-21-123", "system" };

        Assert.False(AutoStartService.IsHelperRequest(args, out _, out _, out _, out _));
    }

    [Fact]
    public void BuildTaskAction_QuotesExecutableAndAddsSilentArgument()
    {
        string action = AutoStartService.BuildTaskAction(@"C:\Program Files\SmartWindowTool\SmartWindowTool.exe");

        Assert.Equal("\"C:\\Program Files\\SmartWindowTool\\SmartWindowTool.exe\" --autostart", action);
    }

    [Fact]
    public void GetTaskName_IsScopedToUserSid()
    {
        Assert.Equal("SmartWindowTool-AutoStart-S-1-5-21-123", AutoStartService.GetTaskName("S-1-5-21-123"));
    }

    [Theory]
    [InlineData("--autostart", true)]
    [InlineData("--AUTOSTART", true)]
    [InlineData("--replace-instance", false)]
    public void IsAutoStartLaunch_DetectsArgument(string argument, bool expected)
    {
        Assert.Equal(expected, AutoStartService.IsAutoStartLaunch(new[] { argument }));
    }

    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    public void CombineConfigurationStates_PreservesConfiguredAndUnknownResults(
        int first,
        int second,
        int expected)
    {
        Assert.Equal(
            (AutoStartConfigurationState)expected,
            AutoStartService.CombineConfigurationStates(
                (AutoStartConfigurationState)first,
                (AutoStartConfigurationState)second));
    }

}
