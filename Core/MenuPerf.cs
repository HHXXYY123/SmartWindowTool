using System;
using System.IO;

namespace SmartWindowTool.Core
{
    /// <summary>
    /// 菜单性能诊断跟踪：仅当 %APPDATA%\SmartWindowTool\perf-trace.flag 存在时写入 perf-trace.log，
    /// 平时零开销。用于排查呼出卡顿、钩子失效类问题。
    /// </summary>
    internal static class MenuPerf
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartWindowTool");

        internal static bool TraceEnabled => File.Exists(Path.Combine(Dir, "perf-trace.flag"));

        internal static void Trace(string message)
        {
            if (!TraceEnabled) return;
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "perf-trace.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
