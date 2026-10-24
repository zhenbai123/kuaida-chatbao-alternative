using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using QuickReplyLite;

internal static class Tests
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    private static void Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "quick-reply-lite-" + Guid.NewGuid().ToString("N"));
        try
        {
            LocalStore store = new LocalStore(root);
            Check(store.Data.Phrases.Count == 3 && File.Exists(store.FilePath), "first launch creates readable local JSON and examples");
            Category category = store.AddCategory("售后服务");
            Reject(delegate { store.AddCategory("售后服务"); }, "duplicate categories are rejected");
            string original = Path.Combine(root, "产品说明.txt"); File.WriteAllText(original, "中文附件内容");
            Attachment attachment = store.CopyAttachment(original);
            File.Delete(original);
            Check(File.ReadAllText(store.Resolve(attachment)) == "中文附件内容", "copied attachments survive original file removal");
            Phrase phrase = new Phrase { Id = Guid.NewGuid().ToString("N"), CategoryId = category.Id, Keyword = "退款", ColorHex = "#2878D0", Text = "您好，已记录。\r\n我们会尽快处理。" };
            phrase.Attachments.Add(attachment); store.Data.Phrases.Add(phrase); store.Save();
            Check(store.Search(category.Id, "退款").Count == 1 && store.Search(category.Id, "产品说明").Count == 1, "search covers keywords, text and attachment names");
            LocalStore reopened = new LocalStore(root);
            Phrase saved = reopened.Data.Phrases.First(p => p.Id == phrase.Id);
            Check(saved.Text == phrase.Text && saved.Attachments[0].Name == "产品说明.txt", "Chinese text, newlines and attachments round-trip through JSON");
            reopened.Data.Preferences.Locked = true; reopened.Data.Preferences.X = 200; reopened.Data.Preferences.AutoSend = false; reopened.Save();
            LocalStore restored = new LocalStore(root);
            Check(restored.Data.Preferences.Locked && restored.Data.Preferences.X == 200 && !restored.Data.Preferences.AutoSend, "floating-window and send settings persist");
            Directory.CreateDirectory(restored.FilePath + ".tmp");
            bool addFailed = false, deleteFailed = false;
            try { restored.AddCategory("保存失败测试"); } catch (IOException) { addFailed = true; } catch (UnauthorizedAccessException) { addFailed = true; }
            try { restored.DeleteCategory(category.Id); } catch (IOException) { deleteFailed = true; } catch (UnauthorizedAccessException) { deleteFailed = true; }
            Check(addFailed && deleteFailed && !restored.Data.Categories.Any(c => c.Name == "保存失败测试") &&
                restored.Data.Categories.Any(c => c.Id == category.Id) && restored.Data.Phrases.First(p => p.Id == phrase.Id).CategoryId == category.Id,
                "failed saves roll back category creation, deletion and phrase moves");
            Directory.Delete(restored.FilePath + ".tmp");
            restored.DeleteCategory(category.Id);
            Check(restored.Data.Phrases.Any(p => p.Id == phrase.Id) && restored.Data.Phrases.All(p => p.CategoryId != category.Id), "deleting a category preserves its phrases");
            Attachment unsafePath = new Attachment { Name = "outside", RelativePath = "素材/../../secret.txt" };
            Reject(delegate { restored.Resolve(unsafePath); }, "path traversal is rejected");
            Reject(delegate { restored.Resolve(new Attachment { Name = "outside", RelativePath = "C:\\secret.txt" }); }, "absolute Windows paths are rejected");
            string beforeCorrupt = restored.FilePath;
            File.WriteAllText(beforeCorrupt, "invalid json");
            LocalStore recovered = new LocalStore(root);
            Check(recovered.RecoveredBackup && recovered.Data.Phrases.Count >= 3 && Directory.GetFiles(root, "*.损坏-*").Length == 1, "corrupt primary JSON recovers backup and preserves the damaged original");
            Phrase fileOnly = new Phrase { Id = Guid.NewGuid().ToString("N"), CategoryId = recovered.Data.Categories[0].Id, Keyword = "材料", Text = "", ColorHex = "" };
            fileOnly.Attachments.Add(attachment); recovered.Data.Phrases.Add(fileOnly); recovered.Save();
            Check(LocalStore.Preview(fileOnly) == "产品说明.txt", "attachment-only phrases are supported");
            CheckEditingFeatures(Path.Combine(root, "editing"));
            TransferTests.Run(Path.Combine(root, "transfer"), Check);
            BangkefuTests.Run(Path.Combine(root, "bangkefu"), Check);
            InteractionTests.Run(Path.Combine(root, "interaction"), Check);
            SettingsTests.Run(Path.Combine(root, "settings"), Check);
            DesktopShortcutTests.Run(Path.Combine(root, "desktop-shortcut"), Check);
            for (int i = 0; i < args.Length; i += 2) BangkefuTests.RealFile(args[i], args.Length > i + 1 ? args[i + 1] : Path.Combine(root, "converted-" + i + ".quickreply"), Path.Combine(root, "real-library-" + i), Check);
            Console.WriteLine("All " + checks + " checks passed.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void CheckEditingFeatures(string folder)
    {
        LocalStore store = new LocalStore(folder);
        Category extra = store.AddCategory("其他分类");
        store.Data.Phrases.Clear();
        string[] texts = { "真的很实惠", "这个东西真的假的", "真的真的\r\n原文保留。", "真 的", "没有命中" };
        for (int i = 0; i < texts.Length; i++) store.Data.Phrases.Add(new Phrase { Id = "p" + i, CategoryId = i < 2 ? store.Data.Categories[0].Id : extra.Id, Text = texts[i], Keyword = "真的", ColorHex = "#E64A45" });
        string original = Path.Combine(folder, "真的.txt"); File.WriteAllText(original, "真的附件内容");
        Attachment attachment = store.CopyAttachment(original); store.Data.Phrases[4].Attachments.Add(attachment); store.Save();
        List<TextReplacement> preview = store.PreviewTextReplacement("真的", "肯定");
        Check(preview.Count == 3 && preview.Sum(p => p.Occurrences) == 4 && store.Data.Phrases[0].Text == texts[0], "replacement preview searches body across categories without mutating data");
        Check(preview[0].After == "肯定很实惠" && preview[1].After == "这个东西肯定假的" && preview[2].After == "肯定肯定\r\n原文保留。", "literal Chinese replacement changes only exact occurrences and preserves newlines");
        Check(store.ApplyTextReplacements(preview) == 3 && store.Data.Phrases[3].Text == "真 的" && store.Data.Phrases[4].Text == "没有命中" &&
            store.Data.Phrases.All(p => p.Keyword == "真的" && p.ColorHex == "#E64A45") && store.Data.Phrases[4].Attachments[0] == attachment && File.ReadAllText(store.Resolve(attachment)) == "真的附件内容",
            "batch apply preserves unmatched text, keywords, colors and attachment bytes");
        string snapshot = Directory.GetFiles(folder, "话术.批量替换前-*.json").Single();
        Check(LocalStore.Read(snapshot).Phrases[0].Text == texts[0] && new LocalStore(folder).Data.Phrases[1].Text == "这个东西肯定假的", "batch replacement persists and keeps a separate pre-replacement backup");
        Reject(delegate { store.PreviewTextReplacement("", "新词"); }, "empty replacement search is rejected");
        Check(store.PreviewTextReplacement(" 的", "").Single().After == "真", "literal whitespace in search is preserved");
        store.Data.Phrases[3].Text = "a.b $1 A.B"; store.Save();
        Check(store.PreviewTextReplacement("a.b", "$1").Single().After == "$1 $1 A.B", "punctuation and dollar signs are literal and case is exact");
        store.Data.Phrases[3].Text = "aaaa"; store.Save();
        TextReplacement overlap = store.PreviewTextReplacement("aa", "b").Single();
        Check(overlap.After == "bb" && overlap.Occurrences == 2, "repeated substrings use non-overlapping literal replacement");
        Reject(delegate { store.PreviewTextReplacement("a", new string('长', 20000)); }, "oversized replacement previews are rejected before allocating expanded text");
        Check(store.ApplyTextReplacements(store.PreviewTextReplacement("肯定", "肯定")) == 0 && store.ApplyTextReplacements(store.PreviewTextReplacement("不存在", "新")) == 0, "unchanged and zero-match replacements are no-ops");
        preview = store.PreviewTextReplacement("肯定", "一定"); store.Data.Phrases[1].Text += "。";
        Reject(delegate { store.ApplyTextReplacements(preview); }, "stale replacement previews are rejected before any changes");
        Check(store.Data.Phrases[0].Text == "肯定很实惠", "a stale preview cannot partially modify earlier phrases");
        store.Data.Phrases[3].Text = "替空"; store.Data.Phrases[4].Text = "替空内容"; store.Save();
        Reject(delegate { store.ApplyTextReplacements(store.PreviewTextReplacement("替空", "")); }, "replacement cannot leave a phrase with no text or attachment");
        Check(store.Data.Phrases[3].Text == "替空" && store.Data.Phrases[4].Text == "替空内容", "invalid batch changes roll back every affected phrase");
        Check(store.MovePhrase("p0", "p4", true) && String.Join(",", store.Data.Phrases.Select(p => p.Id)) == "p1,p2,p3,p4,p0", "dragging downward inserts after an arbitrary target");
        Check(store.MovePhrase("p0", "p1", false) && String.Join(",", store.Data.Phrases.Select(p => p.Id)) == "p0,p1,p2,p3,p4", "dragging upward inserts before an arbitrary target");
        Check(!store.MovePhrase("p0", "p0", false) && !store.MovePhrase("p0", "p1", false), "dropping at the same position is a no-op");
        Check(store.MovePhrase("p0", "p2", false) && String.Join(",", new LocalStore(folder).Data.Phrases.Select(p => p.Id)) == "p1,p0,p2,p3,p4", "filtered-view anchors preserve other phrases and persist the new order");
        Reject(delegate { store.MovePhrase("missing", "p1", false); }, "unknown drag targets cannot modify the order");
        string order = String.Join(",", store.Data.Phrases.Select(p => p.Id));
        Directory.CreateDirectory(store.FilePath + ".tmp");
        Reject(delegate { store.MovePhrase("p3", "p0", true); }, "reordering reports save failures");
        Check(String.Join(",", store.Data.Phrases.Select(p => p.Id)) == order, "failed reordering restores the complete original order");
        Reject(delegate { store.ApplyTextReplacements(store.PreviewTextReplacement("肯定", "一定")); }, "batch replacement reports save failures");
        Check(store.Data.Phrases.First(p => p.Id == "p0").Text == "肯定很实惠" && new LocalStore(folder).Data.Phrases.First(p => p.Id == "p0").Text == "肯定很实惠", "failed batch saves preserve both memory and disk text");
        Directory.Delete(store.FilePath + ".tmp");
    }
}
