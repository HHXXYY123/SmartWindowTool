using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SmartWindowTool.Core
{
    public static class AutoStartService
    {
        public const string AutoStartArgument = "--autostart";
        public const string HelperArgument = "--autostart-helper";

        private const string LegacyTaskName = "SmartWindowTool";
        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const int ProcessTimeoutMilliseconds = 10_000;
        private const int TaskStateDisabled = 1;
        private const int FileNotFoundHResult = unchecked((int)0x80070002);

        public static bool IsHelperRequest(
            string[] args,
            out bool enable,
            out bool runAsAdmin,
            out string userName,
            out string userSid)
        {
            enable = false;
            runAsAdmin = false;
            userName = string.Empty;
            userSid = string.Empty;

            if (args.Length != 5 || !args[0].Equals(HelperArgument, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            enable = args[1].Equals("enable", StringComparison.OrdinalIgnoreCase);
            if (!enable && !args[1].Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            userName = args[2];
            userSid = args[3];
            runAsAdmin = args[4].Equals("highest", StringComparison.OrdinalIgnoreCase);
            if (!runAsAdmin && !args[4].Equals("limited", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(userSid);
        }

        public static bool IsAutoStartLaunch(string[] args)
        {
            return Array.Exists(args, arg => arg.Equals(AutoStartArgument, StringComparison.OrdinalIgnoreCase));
        }

        internal static AutoStartConfigurationState GetConfigurationStateForCurrentUser()
        {
            return CombineConfigurationStates(
                GetScheduledTaskState(GetTaskName(GetCurrentUserSid())),
                GetLegacyTaskConfigurationState(),
                GetLegacyRegistryConfigurationState());
        }

        public static async Task<AutoStartResult> ConfigureForCurrentUserAsync(bool enable, bool runAsAdmin)
        {
            string userName = WindowsIdentity.GetCurrent().Name;
            string userSid = GetCurrentUserSid();

            if (IsCurrentProcessElevated())
            {
                int exitCode = await Task.Run(() => RunElevatedHelper(enable, runAsAdmin, userName, userSid));
                return CompleteConfiguration(FromHelperExitCode(exitCode));
            }

            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return new AutoStartResult(false, "无法确定当前程序路径。");
            }

            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add(HelperArgument);
            startInfo.ArgumentList.Add(enable ? "enable" : "disable");
            startInfo.ArgumentList.Add(userName);
            startInfo.ArgumentList.Add(userSid);
            startInfo.ArgumentList.Add(runAsAdmin ? "highest" : "limited");

            try
            {
                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return new AutoStartResult(false, "无法启动自启动配置助手。");
                }

                await process.WaitForExitAsync();
                return CompleteConfiguration(FromHelperExitCode(process.ExitCode));
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new AutoStartResult(false, "已取消管理员授权，自启动设置未更改。");
            }
            catch (Exception ex)
            {
                return new AutoStartResult(false, $"无法配置自启动：{ex.Message}");
            }
        }

        public static int RunElevatedHelper(bool enable, bool runAsAdmin, string userName, string userSid)
        {
            if (!IsCurrentProcessElevated()) return 10;

            try
            {
                if (!enable)
                {
                    if (!DeleteTaskIfPresent(GetTaskName(userSid))) return 21;
                    if (!DeleteTaskIfPresent(LegacyTaskName)) return 23;
                    return 0;
                }

                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath)) return 11;

                var createResult = RunSchtasks(BuildCreateTaskArguments(
                    executablePath,
                    GetTaskName(userSid),
                    userName,
                    runAsAdmin));
                if (createResult.ExitCode != 0) return 20;

                // Only remove working legacy mechanisms after the replacement task exists.
                DeleteTaskIfPresent(LegacyTaskName);
                return 0;
            }
            catch
            {
                return 12;
            }
        }

        internal static string BuildTaskAction(string executablePath)
        {
            return $"\"{executablePath}\" {AutoStartArgument}";
        }

        internal static string[] BuildCreateTaskArguments(
            string executablePath,
            string taskName,
            string userName,
            bool runAsAdmin)
        {
            return new[]
            {
                "/Create", "/TN", taskName, "/TR", BuildTaskAction(executablePath),
                "/SC", "ONLOGON", "/RU", userName, "/IT", "/RL",
                runAsAdmin ? "HIGHEST" : "LIMITED", "/F"
            };
        }

        internal static string GetTaskName(string userSid)
        {
            return $"SmartWindowTool-AutoStart-{userSid}";
        }

        internal static AutoStartConfigurationState GetLegacyTaskConfigurationState()
        {
            return GetScheduledTaskState(LegacyTaskName);
        }

        internal static AutoStartConfigurationState CombineConfigurationStates(
            params AutoStartConfigurationState[] states)
        {
            if (states.Contains(AutoStartConfigurationState.Configured))
            {
                return AutoStartConfigurationState.Configured;
            }

            return states.Contains(AutoStartConfigurationState.Unknown)
                ? AutoStartConfigurationState.Unknown
                : AutoStartConfigurationState.NotConfigured;
        }

        public static bool IsCurrentProcessElevated()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static bool DeleteTaskIfPresent(string taskName)
        {
            object? service = null;
            object? folder = null;
            try
            {
                Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return DeleteTaskWithSchtasks(taskName);

                service = Activator.CreateInstance(serviceType);
                if (service == null) return DeleteTaskWithSchtasks(taskName);

                ((dynamic)service).Connect();
                folder = ((dynamic)service).GetFolder("\\");
                ((dynamic)folder).DeleteTask(taskName, 0);
                return true;
            }
            catch (Exception ex) when (ex.HResult == FileNotFoundHResult)
            {
                return true;
            }
            catch
            {
                return DeleteTaskWithSchtasks(taskName);
            }
            finally
            {
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        private static bool DeleteTaskWithSchtasks(string taskName)
        {
            ProcessResult result = RunSchtasks(new[] { "/Delete", "/TN", taskName, "/F" });
            if (result.ExitCode == 0) return true;

            // A concurrent cleanup can make schtasks return a non-zero code even though the
            // task is already gone. Check existence rather than enabled state here.
            return IsScheduledTaskMissing(taskName);
        }

        private static bool IsScheduledTaskMissing(string taskName)
        {
            object? service = null;
            object? folder = null;
            object? task = null;
            try
            {
                Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return false;

                service = Activator.CreateInstance(serviceType);
                if (service == null) return false;

                ((dynamic)service).Connect();
                folder = ((dynamic)service).GetFolder("\\");
                task = ((dynamic)folder).GetTask(taskName);
                return false;
            }
            catch (Exception ex) when (ex.HResult == FileNotFoundHResult)
            {
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseComObject(task);
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        private static AutoStartConfigurationState GetScheduledTaskState(string taskName)
        {
            object? service = null;
            object? folder = null;
            object? task = null;
            try
            {
                Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return AutoStartConfigurationState.Unknown;

                service = Activator.CreateInstance(serviceType);
                if (service == null) return AutoStartConfigurationState.Unknown;

                ((dynamic)service).Connect();
                folder = ((dynamic)service).GetFolder("\\");
                task = ((dynamic)folder).GetTask(taskName);

                bool enabled = (bool)((dynamic)task).Enabled;
                int state = (int)((dynamic)task).State;
                return enabled && state != TaskStateDisabled
                    ? AutoStartConfigurationState.Configured
                    : AutoStartConfigurationState.NotConfigured;
            }
            catch (Exception ex) when (ex.HResult == FileNotFoundHResult)
            {
                return AutoStartConfigurationState.NotConfigured;
            }
            catch
            {
                return AutoStartConfigurationState.Unknown;
            }
            finally
            {
                ReleaseComObject(task);
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        private static AutoStartConfigurationState GetLegacyRegistryConfigurationState()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
                return key?.GetValue(LegacyTaskName) is string value && !string.IsNullOrWhiteSpace(value)
                    ? AutoStartConfigurationState.Configured
                    : AutoStartConfigurationState.NotConfigured;
            }
            catch
            {
                return AutoStartConfigurationState.Unknown;
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }

        private static bool DeleteLegacyRegistryValue()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                key?.DeleteValue(LegacyTaskName, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ProcessResult RunSchtasks(IReadOnlyList<string> arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo("schtasks.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process? process = Process.Start(startInfo);
                if (process == null) return new ProcessResult(-1);

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(ProcessTimeoutMilliseconds))
                {
                    process.Kill(true);
                    return new ProcessResult(-2);
                }

                Task.WaitAll(outputTask, errorTask);
                return new ProcessResult(process.ExitCode);
            }
            catch
            {
                return new ProcessResult(-1);
            }
        }

        private static string GetCurrentUserSid()
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("无法获取当前用户 SID。");
        }

        internal static AutoStartResult FromHelperExitCode(int exitCode)
        {
            return exitCode switch
            {
                0 => AutoStartResult.Success,
                10 => new AutoStartResult(false, "配置助手未获得管理员权限。"),
                11 => new AutoStartResult(false, "无法确定当前程序路径。"),
                12 => new AutoStartResult(false, "配置自启动计划任务时发生未知错误。"),
                20 => new AutoStartResult(false, "计划任务创建失败。"),
                21 => new AutoStartResult(false, "当前用户的自启动计划任务删除失败，请确认任务计划程序服务正在运行后重试。"),
                22 => new AutoStartResult(false, "旧版注册表启动项清理失败。"),
                23 => new AutoStartResult(false, "旧版 SmartWindowTool 计划任务删除失败，请在任务计划程序中检查同名任务。"),
                _ => new AutoStartResult(false, $"自启动配置助手失败，退出码：{exitCode}。")
            };
        }

        private static AutoStartResult CompleteConfiguration(AutoStartResult result)
        {
            if (!result.Succeeded) return result;
            return DeleteLegacyRegistryValue()
                ? result
                : new AutoStartResult(false, "计划任务已更新，但旧版注册表启动项清理失败。");
        }

        private readonly record struct ProcessResult(int ExitCode);
    }

    public readonly record struct AutoStartResult(bool Succeeded, string ErrorMessage)
    {
        public static AutoStartResult Success { get; } = new(true, string.Empty);
    }

    internal enum AutoStartConfigurationState
    {
        Unknown,
        NotConfigured,
        Configured
    }
}
