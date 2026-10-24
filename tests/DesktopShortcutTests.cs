using System;
using System.Collections.Generic;
using System.IO;
using QuickReplyLite;

internal static class DesktopShortcutTests
{
    private sealed class MemoryShortcuts : IDesktopShortcuts
    {
        internal readonly Dictionary<string, ShortcutDefinition> Entries = new Dictionary<string, ShortcutDefinition>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal int Writes;
        internal bool FailSave;
        public bool Exists(string path) { return Entries.ContainsKey(path) || Unreadable.Contains(path); }
        public ShortcutDefinition Read(string path) { if (Unreadable.Contains(path)) throw new IOException("unreadable shortcut"); return Entries[path]; }
        public void Save(string path, ShortcutDefinition value) { if (FailSave) throw new UnauthorizedAccessException("desktop denied"); Writes++; Entries[path] = value; }
    }

    internal static void Run(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "中文 空格 程序"), desktop = Path.Combine(root, "重定向 桌面");
        Directory.CreateDirectory(directory); Directory.CreateDirectory(desktop);
        string executable = Path.Combine(directory, "QuickReplyLite.exe"); File.WriteAllBytes(executable, new byte[] { 77, 90 });
        MemoryShortcuts links = new MemoryShortcuts();
        string first = DesktopShortcutRegistration.Create(links, desktop, executable);
        ShortcutDefinition definition = links.Entries[first];
        check(first == Path.Combine(desktop, "快答.lnk") && links.Writes == 1, "desktop shortcut uses the current user's supplied redirected desktop location");
        check(definition.TargetPath == executable && definition.WorkingDirectory == directory && definition.Arguments == "", "desktop shortcut keeps Chinese and spaced executable paths without shell commands or extra arguments");
        check(definition.IconLocation == executable + ",0" && definition.Description == "快答 · 简版", "desktop shortcut uses the application's embedded icon and product description");
        check(DesktopShortcutRegistration.Create(links, desktop, executable) == first && links.Writes == 1, "repeated desktop shortcut creation reuses the existing correct shortcut");
        definition.WorkingDirectory = "old";
        check(DesktopShortcutRegistration.Create(links, desktop, executable) == first && links.Writes == 2 && links.Entries[first].WorkingDirectory == directory, "an existing shortcut to this app is repaired when its working directory is stale");
        ShortcutDefinition unrelated = new ShortcutDefinition { TargetPath = Path.Combine(root, "Other.exe") };
        links.Entries[first] = unrelated;
        string second = DesktopShortcutRegistration.Create(links, desktop, executable);
        check(second == Path.Combine(desktop, "快答 (2).lnk") && Object.ReferenceEquals(links.Entries[first], unrelated), "desktop shortcut creation preserves an unrelated same-name shortcut");
        check(DesktopShortcutRegistration.Create(links, desktop, executable) == second && links.Writes == 3, "same-name collisions remain idempotent on retry");
        links.Entries[first] = new ShortcutDefinition { TargetPath = executable, Arguments = "custom" };
        check(DesktopShortcutRegistration.Create(links, desktop, executable) == second && links.Entries[first].Arguments == "custom", "a shortcut with user-specified launch arguments is not overwritten");
        links.Unreadable.Add(first); links.Entries.Remove(second);
        check(DesktopShortcutRegistration.Create(links, desktop, executable) == second, "an unreadable existing desktop entry is preserved and a separate shortcut is created");
        MemoryShortcuts failure = new MemoryShortcuts { FailSave = true };
        Reject(delegate { DesktopShortcutRegistration.Create(failure, desktop, executable); }, "desktop permission failures are surfaced without reporting a shortcut as saved", check);
        check(failure.Writes == 0 && failure.Entries.Count == 0, "failed desktop shortcut creation leaves existing state unchanged");
        foreach (string bad in new[] { null, "", "invalid\"path", "bad\npath", "bad\0path" })
            Reject(delegate { DesktopShortcutRegistration.Create(links, desktop, bad); }, "invalid shortcut executable paths are rejected", check);
        Reject(delegate { DesktopShortcutRegistration.Create(links, "", executable); }, "missing user desktop locations are rejected", check);
        Reject(delegate { DesktopShortcutRegistration.Create(links, desktop, Path.Combine(root, "missing.exe")); }, "shortcut creation does not point to a missing program", check);
        MemoryShortcuts full = new MemoryShortcuts();
        for (int index = 1; index <= 100; index++) full.Entries[Path.Combine(desktop, index == 1 ? "快答.lnk" : "快答 (" + index + ").lnk")] = unrelated;
        Reject(delegate { DesktopShortcutRegistration.Create(full, desktop, executable); }, "excessive same-name desktop entries fail without overwriting other shortcuts", check);
        check(full.Writes == 0, "shortcut collision exhaustion never overwrites unrelated desktop contents");
    }

    private static void Reject(Action action, string name, Action<bool, string> check)
    { bool rejected = false; try { action(); } catch { rejected = true; } check(rejected, name); }
}
