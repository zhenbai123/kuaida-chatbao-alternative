using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace QuickReplyLite
{
    internal sealed class WindowsDesktopShortcuts : IDesktopShortcuts
    {
        internal static string CreateForCurrentUser(string executable)
        { return DesktopShortcutRegistration.Create(new WindowsDesktopShortcuts(), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), executable); }

        public bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }

        public ShortcutDefinition Read(string path)
        {
            if (!File.Exists(path)) return null;
            object shell = null, link = null;
            try
            {
                shell = OpenShell(); link = Call(shell, "CreateShortcut", path);
                return new ShortcutDefinition { TargetPath = Get(link, "TargetPath"), WorkingDirectory = Get(link, "WorkingDirectory"), Arguments = Get(link, "Arguments"), IconLocation = Get(link, "IconLocation"), Description = Get(link, "Description") };
            }
            finally { Release(link); Release(shell); }
        }

        public void Save(string path, ShortcutDefinition definition)
        {
            string folder = Path.GetDirectoryName(path);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("找不到桌面文件夹，请检查Windows桌面设置。");
            string temporary = Path.Combine(folder, ".quickreply-" + Guid.NewGuid().ToString("N") + ".lnk");
            object shell = null, link = null;
            try
            {
                shell = OpenShell(); link = Call(shell, "CreateShortcut", temporary);
                Set(link, "TargetPath", definition.TargetPath); Set(link, "WorkingDirectory", definition.WorkingDirectory);
                Set(link, "Arguments", definition.Arguments); Set(link, "IconLocation", definition.IconLocation); Set(link, "Description", definition.Description);
                Call(link, "Save"); Release(link); link = null;
                if (!File.Exists(temporary)) throw new IOException("Windows未能保存桌面快捷方式。");
                ShortcutDefinition saved = Read(temporary);
                if (saved == null || !String.Equals(saved.TargetPath, definition.TargetPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("桌面快捷方式的目标路径验证失败。");
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (TargetInvocationException error) { throw new IOException("无法创建桌面快捷方式，请检查桌面写入权限或系统脚本组件。", error.InnerException ?? error); }
            finally { Release(link); Release(shell); if (File.Exists(temporary)) try { File.Delete(temporary); } catch { } }
        }

        private static object OpenShell()
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) throw new IOException("Windows快捷方式组件不可用，请检查系统设置。");
            return Activator.CreateInstance(type);
        }
        private static object Call(object instance, string name, params object[] args) { return instance.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, instance, args); }
        private static string Get(object instance, string name) { return instance.GetType().InvokeMember(name, BindingFlags.GetProperty, null, instance, null) as string ?? ""; }
        private static void Set(object instance, string name, string value) { instance.GetType().InvokeMember(name, BindingFlags.SetProperty, null, instance, new object[] { value }); }
        private static void Release(object instance) { if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance); }
    }
}
