using System;
using System.IO;
using System.Linq;
using QuickReplyLite;

internal static class SettingsTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Startup(check); Destination(Path.Combine(root, "destination"), check);
    }

    private sealed class MemoryStartupEntry : IStartupEntry
    {
        internal string Value;
        internal int Writes, Deletes;
        internal bool FailWrite, FailDelete, IgnoreWrite, FailRead;
        public string Read() { if (FailRead) throw new UnauthorizedAccessException("read denied"); return Value; }
        public void Write(string command)
        {
            Writes++;
            if (FailWrite) { FailWrite = false; throw new UnauthorizedAccessException("write denied"); }
            if (IgnoreWrite) { IgnoreWrite = false; return; }
            Value = command;
        }
        public void Remove() { Deletes++; Value = null; if (FailDelete) { FailDelete = false; throw new IOException("delete failed"); } }
    }

    private static void Startup(Action<bool, string> check)
    {
        string executable = @"C:\客服工具\快答 简版\QuickReplyLite.exe";
        string command = "\"" + executable + "\"";
        MemoryStartupEntry entry = new MemoryStartupEntry();
        check(StartupRegistration.Command(executable) == command, "startup command quotes paths containing spaces and Chinese characters");
        foreach (string invalid in new[] { "", "bad\"path", "bad\npath" }) Reject(delegate { StartupRegistration.Command(invalid); }, "invalid startup command paths are rejected", check);
        check(!StartupRegistration.IsEnabled(entry, executable) && entry.Writes == 0 && entry.Deletes == 0, "reading startup status does not enable startup or write settings");
        StartupRegistration.SetEnabled(entry, executable, true);
        check(entry.Value == command && StartupRegistration.IsEnabled(entry, executable) && entry.Writes == 1, "checking startup saves the current executable command and verifies it");
        StartupRegistration.SetEnabled(entry, executable, true);
        check(entry.Writes == 1, "repeated startup enable is idempotent");
        StartupRegistration.SetEnabled(entry, executable, false); StartupRegistration.SetEnabled(entry, executable, false);
        check(entry.Value == null && entry.Deletes == 1 && !StartupRegistration.IsEnabled(entry, executable), "unchecking startup removes its entry and repeated disable is a no-op");
        entry.Value = command.ToLowerInvariant();
        check(StartupRegistration.IsEnabled(entry, executable), "startup status recognizes case-insensitive Windows paths");
        entry.Value = @"""D:\另一份快答\QuickReplyLite.exe"""; string other = entry.Value; int deletes = entry.Deletes;
        StartupRegistration.SetEnabled(entry, executable, false);
        check(entry.Value == other && entry.Deletes == deletes, "disabling this copy does not remove a different copy's startup registration");
        StartupRegistration.SetEnabled(entry, executable, true);
        check(entry.Value == command, "enabling a relocated copy updates startup to the current executable path");
        entry.Value = other; entry.FailWrite = true;
        Reject(delegate { StartupRegistration.SetEnabled(entry, executable, true); }, "startup write failures are surfaced", check);
        check(entry.Value == other, "failed startup changes restore the previous registration");
        entry.Value = null; entry.IgnoreWrite = true;
        Reject(delegate { StartupRegistration.SetEnabled(entry, executable, true); }, "startup read-back verification detects an unsaved change", check);
        check(entry.Value == null, "a failed startup enable does not leave a false enabled state");
        entry.Value = command; entry.FailDelete = true;
        Reject(delegate { StartupRegistration.SetEnabled(entry, executable, false); }, "startup removal failures are surfaced", check);
        check(entry.Value == command, "a failed startup disable restores the previous enabled state");
        entry.FailRead = true; int writes = entry.Writes;
        Reject(delegate { StartupRegistration.SetEnabled(entry, executable, true); }, "startup read permission errors do not silently change settings", check);
        check(entry.Writes == writes, "startup read failure cannot overwrite an unknown existing setting");
    }

    private static void Destination(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        LocalStore source = new LocalStore(Path.Combine(root, "source")); source.Data.Phrases.Clear();
        Category personal = source.Data.Categories[0], shared = source.AddCategory("常用", Board.PublicId); Board team = source.AddBoard("文件内部门"); Category materials = source.AddCategory("资料", team.Id);
        string asset = Path.Combine(root, "资料.txt"); File.WriteAllText(asset, "保留附件实际内容"); Attachment file = source.CopyAttachment(asset);
        source.Data.Phrases.Add(new Phrase { Id = "personal", CategoryId = personal.Id, Keyword = "个人", Text = "文件内个人正文" });
        Phrase withFile = new Phrase { Id = "public", CategoryId = shared.Id, Keyword = "公共", Text = "文件内公共正文" }; withFile.Attachments.Add(file); source.Data.Phrases.Add(withFile);
        source.Data.Phrases.Add(new Phrase { Id = "team", CategoryId = materials.Id, Keyword = "部门", Text = "文件内部门正文" }); source.Save();
        string package = Path.Combine(root, "multi.quickreply"); LibraryTransfer.ExportPackage(source, package, "");
        LocalStore target = new LocalStore(Path.Combine(root, "target")); Board destination = target.AddBoard("指定大板块"); Category old = target.AddCategory("旧分类", destination.Id), publicOld = target.AddCategory("原公共", Board.PublicId);
        target.Data.Phrases.Add(new Phrase { Id = "old-target", CategoryId = old.Id, Keyword = "旧", Text = "目标板块原话术" });
        target.Data.Phrases.Add(new Phrase { Id = "outside", CategoryId = publicOld.Id, Keyword = "保留", Text = "其他板块保留" }); target.Save();
        using (ImportedLibrary incoming = LibraryTransfer.Load(package))
        {
            ImportResult added = LibraryTransfer.Import(target, incoming, false, true, destination.Id);
            check(incoming.HasBoardLayout && added.Added == 3 && added.AddedBoards == 0 && !target.Data.Boards.Any(b => b.Name == "文件内部门") && target.Search("", "文件内", destination.Id).Count == 3,
                "v3 multi-board packages can be redirected entirely into a selected custom board");
            check(target.Data.Categories.Count(c => c.BoardId == destination.Id && c.Name == "常用") == 1 && target.Data.Phrases.Any(p => p.Id == "outside") && target.Data.Phrases.Any(p => p.Id == "old-target"),
                "redirected append merges source same-name categories inside the target and preserves existing phrases");
            Phrase copied = target.Search("", "文件内公共正文", destination.Id).Single();
            check(File.ReadAllBytes(target.Resolve(copied.Attachments.Single())).SequenceEqual(File.ReadAllBytes(source.Resolve(file))), "redirecting a modern package preserves exact attachment bytes");
            byte[] saved = File.ReadAllBytes(target.FilePath); ImportResult repeated = LibraryTransfer.Import(target, incoming, false, true, destination.Id);
            check(repeated.Skipped == 3 && repeated.Added == 0 && saved.SequenceEqual(File.ReadAllBytes(target.FilePath)), "redirected duplicate detection uses the destination board and does not rewrite unchanged data");
            check(LibraryTransfer.Import(target, incoming, false, true, Board.PersonalId).Added == 3, "the same package can deliberately be imported to a different destination board");
            string[] outsideIds = target.Data.Phrases.Where(p => target.Data.Categories.First(c => c.Id == p.CategoryId).BoardId != destination.Id).Select(p => p.Id).ToArray();
            ImportResult replaced = LibraryTransfer.Import(target, incoming, true, true, destination.Id);
            check(replaced.Added == 3 && target.Search("", "", destination.Id).Count == 3 && !target.Data.Phrases.Any(p => p.Id == "old-target") && outsideIds.All(id => target.Data.Phrases.Any(p => p.Id == id)) && File.Exists(replaced.BackupPath),
                "replace into a selected board replaces only that board and backs up all prior data");
            string beforeBoards = String.Join("|", target.Data.Boards.Select(b => b.Id + b.Name)); string beforeCategories = String.Join("|", target.Data.Categories.Select(c => c.Id + c.BoardId)); saved = File.ReadAllBytes(target.FilePath);
            Directory.CreateDirectory(target.FilePath + ".tmp");
            Reject(delegate { LibraryTransfer.Import(target, incoming, true, true, destination.Id); }, "failed selected-board replacement reports the save failure", check);
            check(saved.SequenceEqual(File.ReadAllBytes(target.FilePath)) && beforeBoards == String.Join("|", target.Data.Boards.Select(b => b.Id + b.Name)) && beforeCategories == String.Join("|", target.Data.Categories.Select(c => c.Id + c.BoardId)),
                "failed selected-board replacement rolls back board structure and existing data");
            Directory.Delete(target.FilePath + ".tmp");
            Reject(delegate { LibraryTransfer.Import(target, incoming, false, true, "not-a-board"); }, "modern package redirection rejects nonexistent destination boards", check);
            check(saved.SequenceEqual(File.ReadAllBytes(target.FilePath)), "invalid destination selection cannot modify existing data");
            LibraryTransfer.Import(target, incoming, true, true, null);
            check(target.Data.Boards.Any(b => b.Name == "文件内部门") && target.Data.Phrases.Count == 3 && target.Search("", "文件内公共正文", Board.PublicId).Count == 1, "keep-original-board mode remains available for complete layout restoration");
        }
        string csv = Path.Combine(root, "board.csv"); LibraryTransfer.ExportCsv(source, csv, "");
        using (ImportedLibrary incoming = LibraryTransfer.Load(csv))
        {
            LocalStore csvTarget = new LocalStore(Path.Combine(root, "csv-target"));
            ImportResult result = LibraryTransfer.Import(csvTarget, incoming, false, true, Board.PublicId);
            check(incoming.HasBoardLayout && result.Added == 3 && csvTarget.Search("", "文件内", Board.PublicId).Count == 3 && !csvTarget.Data.Boards.Any(b => b.Name == "文件内部门"),
                "CSV files containing original board names can also be imported into a chosen public board");
        }
    }

    private static void Reject(Action action, string name, Action<bool, string> check) { bool failed = false; try { action(); } catch { failed = true; } check(failed, name); }
}
