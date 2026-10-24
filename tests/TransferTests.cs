using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using QuickReplyLite;

internal static class TransferTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        Action<Action, string> reject = delegate(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } check(failed, name); };
        LocalStore source = new LocalStore(Path.Combine(root, "source"));
        Category extra = source.AddCategory("产品介绍"); source.Data.Phrases.Clear();
        string picture = Path.Combine(root, "产品图片.png"), document = Path.Combine(root, "资料.txt");
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQ0AAAAASUVORK5CYII=");
        File.WriteAllBytes(picture, png); File.WriteAllText(document, "中文附件内容");
        Attachment image = source.CopyAttachment(picture), file = source.CopyAttachment(document);
        Phrase first = new Phrase { Id = "first", CategoryId = source.Data.Categories[0].Id, Keyword = "问候", ColorHex = "#2878D0", Text = "您好,\r\n\"真的\"很好 😊" }; first.Attachments.Add(image);
        Phrase second = new Phrase { Id = "second", CategoryId = extra.Id, Keyword = "文件", ColorHex = "#E64A45", Text = "" }; second.Attachments.Add(file);
        Phrase third = new Phrase { Id = "third", CategoryId = extra.Id, Keyword = "详情", ColorHex = "", Text = "了解详情" }; third.Attachments.Add(image);
        source.Data.Phrases.AddRange(new[] { first, second, third }); source.Data.Preferences.X = 999; source.Data.Preferences.Locked = true; source.Save();
        string all = Path.Combine(root, "全部.quickreply"), category = Path.Combine(root, "分类.quickreply"), csv = Path.Combine(root, "文字.csv");
        LibraryTransfer.ExportPackage(source, all, ""); LibraryTransfer.ExportPackage(source, category, extra.Id);
        using (ImportedLibrary one = LibraryTransfer.Load(category)) check(one.Library.Categories.Count == 1 && one.Library.Phrases.Count == 2 && one.FileCount == 2, "category export contains only that category and its actual attachments");
        check(LibraryTransfer.ExportCsv(source, csv, "") == 2, "CSV export includes text phrases and excludes attachment-only rows");
        using (ImportedLibrary table = LibraryTransfer.Load(csv))
        {
            check(table.Library.Phrases[0].Text == first.Text && table.Library.Phrases[0].Keyword == first.Keyword && table.Library.Phrases[0].ColorHex == first.ColorHex && table.FileCount == 0, "CSV round-trip preserves Chinese, emoji, quotes, commas, newlines and colors");
        }
        check(File.ReadAllBytes(csv).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "CSV export writes a UTF-8 BOM for Windows spreadsheet apps");
        Directory.Delete(Path.Combine(source.Folder, "素材"), true); File.Delete(picture); File.Delete(document);
        string staged;
        using (ImportedLibrary incoming = LibraryTransfer.Load(all))
        {
            staged = incoming.Sources.Values.First();
            check(incoming.Library.Phrases.Count == 3 && incoming.FileCount == 2 && File.ReadAllBytes(incoming.Sources[image.RelativePath]).SequenceEqual(png), "portable package contains the full attachment bytes and survives loss of original files");
            LocalStore target = new LocalStore(Path.Combine(root, "target"));
            string[] oldIds = target.Data.Phrases.Select(p => p.Id).ToArray(); target.Data.Preferences.X = 123; target.Data.Preferences.Locked = false; target.Save(); Preferences preferences = target.Data.Preferences;
            ImportResult appended = LibraryTransfer.Import(target, incoming, false, true);
            check(appended.Added == 3 && appended.AddedCategories == 1 && target.Data.Phrases.Count == 6 && oldIds.All(id => target.Data.Phrases.Any(p => p.Id == id)), "append import preserves existing phrases and merges same-name categories");
            Phrase imported = target.Data.Phrases.First(p => p.Keyword == "问候" && p.Text == first.Text);
            check(imported.Id != first.Id && imported.Text == first.Text && imported.ColorHex == first.ColorHex && File.ReadAllBytes(target.Resolve(imported.Attachments[0])).SequenceEqual(png), "import assigns independent IDs and retains text, colors and image bytes");
            check(ReferenceEquals(preferences, target.Data.Preferences) && preferences.X == 123 && !preferences.Locked, "import never overwrites personal floating-window preferences");
            check(File.Exists(appended.BackupPath) && LocalStore.Read(appended.BackupPath).Phrases.Count == 3, "append import keeps an independent pre-import backup");
            byte[] beforeDuplicate = File.ReadAllBytes(target.FilePath);
            ImportResult duplicate = LibraryTransfer.Import(target, incoming, false, true);
            check(duplicate.Added == 0 && duplicate.Skipped == 3 && target.Data.Phrases.Count == 6 && beforeDuplicate.SequenceEqual(File.ReadAllBytes(target.FilePath)), "re-import skips exact duplicates without rewriting existing data");
            check(LibraryTransfer.Import(target, incoming, false, false).Added == 3 && target.Data.Phrases.Count == 9, "duplicate skipping can be disabled for deliberate repeated phrases");
            string oldImage = target.Resolve(imported.Attachments[0]);
            ImportResult replaced = LibraryTransfer.Import(target, incoming, true, true);
            check(replaced.Added == 3 && target.Data.Phrases.Select(p => p.Text).SequenceEqual(incoming.Library.Phrases.Select(p => p.Text)) && target.Data.Categories.Select(c => c.Name).SequenceEqual(incoming.Library.Categories.Select(c => c.Name)), "replace import restores category and phrase order from the package");
            check(File.Exists(oldImage) && LocalStore.Read(replaced.BackupPath).Phrases.Count == 9, "replace retains old attachment copies so the pre-import backup remains restorable");
            check(new LocalStore(target.Folder).Data.Phrases.Count == 3 && target.Data.Phrases.First(p => p.Keyword == "文件").Text == "", "imported attachment-only phrases persist across restart");
            int count = target.Data.Phrases.Count; string ids = String.Join(",", target.Data.Phrases.Select(p => p.Id));
            byte[] disk = File.ReadAllBytes(target.FilePath); int filesBefore = Directory.GetFiles(Path.Combine(target.Folder, "素材"), "*", SearchOption.AllDirectories).Length;
            Directory.CreateDirectory(target.FilePath + ".tmp");
            reject(delegate { LibraryTransfer.Import(target, incoming, true, false); }, "import reports a failed atomic save");
            check(target.Data.Phrases.Count == count && ids == String.Join(",", target.Data.Phrases.Select(p => p.Id)) && disk.SequenceEqual(File.ReadAllBytes(target.FilePath)), "failed import restores original in-memory and on-disk phrases");
            check(Directory.GetFiles(Path.Combine(target.Folder, "素材"), "*", SearchOption.AllDirectories).Length == filesBefore, "failed import cleans up only its newly copied attachments");
            Directory.Delete(target.FilePath + ".tmp");
            reject(delegate { LibraryTransfer.ExportPackage(target, target.FilePath, ""); }, "exports cannot overwrite the live data folder");
        }
        check(!File.Exists(staged), "closing import preview removes staged temporary files");
        string corrupt = Path.Combine(root, "损坏.quickreply");
        Rewrite(all, corrupt, false);
        reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(corrupt)) { } }, "attachment checksum rejects corrupt package bytes");
        Rewrite(all, corrupt, true);
        reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(corrupt)) { } }, "missing packaged attachments are rejected");
        string traversal = Path.Combine(root, "越界.quickreply");
        using (FileStream stream = File.Create(traversal)) using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create)) using (Stream entry = zip.CreateEntry("../outside.txt").Open()) entry.WriteByte(1);
        reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(traversal)) { } }, "archive path traversal is rejected before extraction");
        check(!File.Exists(Path.Combine(root, "outside.txt")), "invalid package paths cannot create files outside staging");
        string proprietary = Path.Combine(root, "原格式.bangkefu"); File.WriteAllBytes(proprietary, Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(proprietary)) { } }, "unrecognized proprietary bangkefu data is explicitly rejected rather than silently imported");
        check(File.ReadAllBytes(proprietary).SequenceEqual(Enumerable.Range(1, 32).Select(i => (byte)i)), "unsupported source files are left untouched");
        string aliases = Path.Combine(root, "旧表格.csv"); File.WriteAllText(aliases, "一级分类,二级分类,回复问题,快捷回复内容\r\n售后,退货,流程,请提供订单号\r\n", new UTF8Encoding(true));
        using (ImportedLibrary table = LibraryTransfer.Load(aliases)) check(table.Library.Categories[0].Name == "售后 / 退货" && table.Library.Phrases[0].Keyword == "流程", "CSV accepts common knowledge-base export column names and two-level categories");
        File.WriteAllText(aliases, "分类,话术内容\r\n常用,\"未闭合", new UTF8Encoding(true));
        reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(aliases)) { } }, "malformed CSV quotes are rejected without partial import");
        string existing = Path.Combine(root, "保留.quickreply"); File.WriteAllText(existing, "existing backup");
        reject(delegate { LibraryTransfer.ExportPackage(source, existing, ""); }, "missing source attachments abort export");
        check(File.ReadAllText(existing) == "existing backup", "a failed export cannot overwrite an existing backup");
    }

    private static void Rewrite(string input, string output, bool omit)
    {
        using (FileStream source = File.OpenRead(input)) using (ZipArchive original = new ZipArchive(source, ZipArchiveMode.Read))
        using (FileStream destination = File.Create(output)) using (ZipArchive changed = new ZipArchive(destination, ZipArchiveMode.Create))
        {
            bool altered = false;
            foreach (ZipArchiveEntry entry in original.Entries)
            {
                bool mutate = !altered && entry.FullName.StartsWith("素材/");
                if (mutate) altered = true;
                if (mutate && omit) continue;
                using (Stream read = entry.Open()) using (MemoryStream bytes = new MemoryStream())
                {
                    read.CopyTo(bytes); byte[] data = bytes.ToArray(); if (mutate && data.Length > 0) data[0] ^= 0x7f;
                    using (Stream write = changed.CreateEntry(entry.FullName).Open()) write.Write(data, 0, data.Length);
                }
            }
        }
    }
}
