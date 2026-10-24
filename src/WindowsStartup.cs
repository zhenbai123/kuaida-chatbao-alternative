using Microsoft.Win32;

namespace QuickReplyLite
{
    internal sealed class WindowsStartupEntry : IStartupEntry
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "QuickReplyLite";
        public string Read()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                return key == null ? null : key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        public void Write(string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        public void Remove()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath, true)) if (key != null) key.DeleteValue(ValueName, false);
        }
    }
}
