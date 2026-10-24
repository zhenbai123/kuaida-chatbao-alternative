using System;

namespace QuickReplyLite
{
    public interface IStartupEntry
    {
        string Read();
        void Write(string command);
        void Remove();
    }

    public static class StartupRegistration
    {
        public static string Command(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("程序路径无效，无法设置开机启动。");
            return "\"" + executable + "\"";
        }

        public static bool IsEnabled(IStartupEntry entry, string executable)
        { return String.Equals((entry.Read() ?? "").Trim(), Command(executable), StringComparison.OrdinalIgnoreCase); }

        public static void SetEnabled(IStartupEntry entry, string executable, bool enabled)
        {
            string command = Command(executable), previous = entry.Read();
            bool currentlyEnabled = String.Equals((previous ?? "").Trim(), command, StringComparison.OrdinalIgnoreCase);
            if (enabled == currentlyEnabled) return;
            try
            {
                if (enabled) entry.Write(command); else entry.Remove();
                if (IsEnabled(entry, executable) != enabled) throw new InvalidOperationException("开机启动设置未能保存，请重试。");
            }
            catch
            {
                try
                {
                    if (!String.Equals(entry.Read(), previous, StringComparison.Ordinal))
                    {
                        if (previous == null) entry.Remove(); else entry.Write(previous);
                        if (!String.Equals(entry.Read(), previous, StringComparison.Ordinal)) throw new InvalidOperationException();
                    }
                }
                catch { throw new InvalidOperationException("开机启动设置失败，且无法恢复原设置。请在Windows启动应用中核对快答。"); }
                throw;
            }
        }
    }
}
