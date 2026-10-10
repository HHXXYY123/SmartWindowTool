using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartWindowTool.Models
{
    public enum BlacklistMode
    {
        Full,
        FunctionOnly
    }

    [JsonConverter(typeof(BlacklistEntryJsonConverter))]
    public class BlacklistEntry : INotifyPropertyChanged
    {
        private string _entry = string.Empty;
        private BlacklistMode _mode = BlacklistMode.Full;
        private bool _isEnabled = true;

        public string Entry
        {
            get => _entry;
            set
            {
                if (string.Equals(_entry, value, StringComparison.Ordinal)) return;
                _entry = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Entry)));
            }
        }

        public BlacklistMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFull)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFunctionOnly)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModeDisplay)));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModeDisplay)));
            }
        }

        [JsonIgnore]
        public bool IsFull
        {
            get => Mode == BlacklistMode.Full;
            set { if (value) Mode = BlacklistMode.Full; }
        }

        [JsonIgnore]
        public bool IsFunctionOnly
        {
            get => Mode == BlacklistMode.FunctionOnly;
            set { if (value) Mode = BlacklistMode.FunctionOnly; }
        }

        [JsonIgnore]
        public string ModeDisplay => !IsEnabled
            ? "已停用"
            : Mode == BlacklistMode.Full ? "完全黑名单" : "仅功能黑名单";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>兼容旧版本将黑名单保存为字符串数组，同时保存新模式字段。</summary>
    public sealed class BlacklistEntryJsonConverter : JsonConverter<BlacklistEntry>
    {
        public override BlacklistEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new BlacklistEntry { Entry = reader.GetString() ?? string.Empty };
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            var result = new BlacklistEntry
            {
                Entry = root.TryGetProperty("Entry", out JsonElement entry)
                    ? entry.GetString() ?? string.Empty
                    : root.TryGetProperty("ProcessName", out JsonElement legacyName)
                        ? legacyName.GetString() ?? string.Empty
                        : string.Empty,
                IsEnabled = !root.TryGetProperty("IsEnabled", out JsonElement enabled) || enabled.GetBoolean()
            };

            if (root.TryGetProperty("Mode", out JsonElement mode))
            {
                if (mode.ValueKind == JsonValueKind.String && Enum.TryParse(mode.GetString(), true, out BlacklistMode parsedMode))
                {
                    result.Mode = parsedMode;
                }
                else if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out int numericMode))
                {
                    result.Mode = (BlacklistMode)numericMode;
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, BlacklistEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Entry", value.Entry);
            writer.WriteString("Mode", value.Mode.ToString());
            writer.WriteBoolean("IsEnabled", value.IsEnabled);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// 进程黑名单匹配器。
    ///
    /// 旧实现只把「裸进程名 + .exe」（如 javaw.exe）与用户条目做 OrdinalIgnoreCase 等值比较，
    /// 因此只要条目写成完整路径（通过「选择进程」或直接粘贴 D:\...\javaw.exe）就永远匹配不到。
    /// 这里同时接受两种写法：
    ///   - 进程名 / 可执行文件名：javaw、javaw.exe
    ///   - 完整路径：C:\Program Files\Eclipse Adoptium\jdk-21.0.11.10-hotspot\bin\javaw.exe
    /// </summary>
    public static class BlacklistMatcher
    {
        public static bool IsBlacklisted(string? processName, string? processPath, IEnumerable<string>? entries)
        {
            if (entries == null) return false;
            foreach (string entry in entries)
            {
                if (Matches(processName, processPath, entry)) return true;
            }
            return false;
        }

        public static BlacklistEntry? FindMatch(
            string? processName,
            string? processPath,
            IEnumerable<BlacklistEntry>? entries)
        {
            if (entries == null) return null;
            foreach (var entry in entries)
            {
                if (entry != null && entry.IsEnabled && Matches(processName, processPath, entry.Entry)) return entry;
            }
            return null;
        }

        public static bool Matches(string? processName, string? processPath, string? rawEntry)
        {
            string entry = NormalizeEntry(rawEntry);
            if (entry.Length == 0) return false;

            string name = (processName ?? string.Empty).Trim();
            string path = (processPath ?? string.Empty).Trim();

            if (LooksLikePath(entry))
            {
                if (path.Length == 0) return false;
                string normalizedEntry = NormalizePath(entry);
                string normalizedPath = NormalizePath(path);
                if (normalizedEntry.Length == 0 || normalizedPath.Length == 0) return false;
                return string.Equals(normalizedPath, normalizedEntry, StringComparison.OrdinalIgnoreCase);
            }

            string entryFileName = Path.GetFileName(entry);
            if (entryFileName.Length == 0) return false;

            // 进程名（无扩展名）与条目比较
            if (name.Length > 0 &&
                string.Equals(StripExe(name), StripExe(entryFileName), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 完整路径的文件名与条目比较（用户在"选择进程"里只拿到文件名时依然有效）
            if (path.Length > 0)
            {
                string pathFileName = Path.GetFileName(path);
                if (pathFileName.Length > 0 &&
                    string.Equals(StripExe(pathFileName), StripExe(entryFileName), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>去掉首尾空白与包裹的引号，用户从资源管理器复制路径时经常带引号。</summary>
        public static string NormalizeEntry(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return raw.Trim().Trim('"').Trim();
        }

        private static string StripExe(string value)
        {
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 4)
                : value;
        }

        private static bool LooksLikePath(string entry)
        {
            return entry.IndexOf('\\') >= 0 || entry.IndexOf('/') >= 0;
        }

        private static string NormalizePath(string value)
        {
            string s = value.Trim().Trim('"');
            try
            {
                s = Path.GetFullPath(s);
            }
            catch
            {
                // 非法路径就按原字符串比较
            }
            return s.TrimEnd('\\', '/');
        }
    }
}
