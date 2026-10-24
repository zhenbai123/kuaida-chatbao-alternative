using System;
using System.IO;

namespace QuickReplyLite
{
    public sealed class ShortcutDefinition
    {
        public string TargetPath { get; set; }
        public string WorkingDirectory { get; set; }
        public string Arguments { get; set; }
        public string IconLocation { get; set; }
        public string Description { get; set; }
    }

    public interface IDesktopShortcuts
    {
        bool Exists(string path);
        ShortcutDefinition Read(string path);
        void Save(string path, ShortcutDefinition definition);
    }

    public static class DesktopShortcutRegistration
    {
        public static string Create(IDesktopShortcuts shortcuts, string desktopDirectory, string executable)
        {
            if (shortcuts == null) throw new ArgumentNullException("shortcuts");
            if (String.IsNullOrWhiteSpace(desktopDirectory)) throw new IOException("无法找到当前用户的桌面文件夹。");
            if (String.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("程序路径无效，无法创建桌面快捷方式。");
            string target = Path.GetFullPath(executable), desktop = Path.GetFullPath(desktopDirectory);
            if (!File.Exists(target)) throw new FileNotFoundException("请先把快答完整解压到固定目录，再创建桌面快捷方式。", target);
            ShortcutDefinition wanted = new ShortcutDefinition {
                TargetPath = target, WorkingDirectory = Path.GetDirectoryName(target), Arguments = "", IconLocation = target + ",0", Description = "快答 · 简版"
            };
            for (int index = 1; index <= 100; index++)
            {
                string path = Path.Combine(desktop, index == 1 ? "快答.lnk" : "快答 (" + index + ").lnk");
                if (!shortcuts.Exists(path)) { shortcuts.Save(path, wanted); return path; }
                ShortcutDefinition previous;
                try { previous = shortcuts.Read(path); } catch { continue; }
                if (previous == null || !EqualPath(previous.TargetPath, target) || !String.IsNullOrEmpty(previous.Arguments)) continue;
                if (EqualPath(previous.WorkingDirectory, wanted.WorkingDirectory) && String.Equals(previous.IconLocation, wanted.IconLocation, StringComparison.OrdinalIgnoreCase) && previous.Description == wanted.Description) return path;
                shortcuts.Save(path, wanted); return path;
            }
            throw new IOException("桌面上同名快捷方式过多，请整理后重试。");
        }

        private static bool EqualPath(string first, string second)
        { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }
}
