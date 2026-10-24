using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using QuickReplyLite;

internal static class InteractionTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string[] lines = PreviewText.Wrap(new string('字', 60)).Split(new[] { "\r\n" }, StringSplitOptions.None);
        check(lines.Select(l => l.Length).SequenceEqual(new[] { 25, 25, 10 }) && String.Join("", lines) == new string('字', 60), "hover preview wraps Chinese at 25 characters without losing content");
        check(PreviewText.Wrap("第一行\n\n第三行\r第四行") == "第一行\r\n\r\n第三行\r\n第四行", "hover preview preserves intentional line breaks and empty lines");
        string unicode = String.Concat(Enumerable.Repeat("😊e\u0301", 30));
        lines = PreviewText.Wrap(unicode).Split(new[] { "\r\n" }, StringSplitOptions.None);
        check(String.Join("", lines) == unicode && lines.All(l => new StringInfo(l).LengthInTextElements <= 25) && lines.All(l => l.Length == 0 || !Char.IsLowSurrogate(l[0])), "hover wrapping keeps emoji surrogate pairs and combining characters together");
        string longText = new string('长', 10000), preview = PreviewText.Wrap(longText);
        lines = preview.Split(new[] { "\r\n" }, StringSplitOptions.None);
        check(lines.Length == 16 && lines.All(l => l.Length <= 25) && preview.EndsWith("…"), "long hover previews stay within 16 lines and mark truncation");
        Phrase phrase = new Phrase { Id = "p", CategoryId = "c", Text = "前文#图1#后文", Keyword = "关键词", ColorHex = "#2878D0", InlineAttachments = true };
        phrase.Attachments.Add(new Attachment { Name = "图片.png", RelativePath = "素材/p.png" });
        string tip = PreviewText.ForPhrase(phrase);
        check(tip.Contains("前文后文") && tip.Contains("图片.png") && !tip.Contains("#图1#") && phrase.Text == "前文#图1#后文", "hover preview shows readable text and attachment names without changing stored image positions");
        phrase.Text = longText;
        tip = PreviewText.ForPhrase(phrase);
        check(tip.Split(new[] { "\r\n" }, StringSplitOptions.None).Length <= 16 && tip.Contains("双击仅粘贴") && tip.Contains("右键或F2编辑"), "long previews retain concise interaction instructions within the height limit");

        phrase.Text = "前文#图1#后文";
        var plan = DeliveryPlan.Build(phrase, DeliveryMode.PasteOnly);
        check(plan.Count == 2 && plan.All(s => !s.PressEnter) && plan[0].Text == "前文后文" && plan[1].AttachmentIndices.SequenceEqual(new[] { 0 }), "double-click paste-only mode prepares mixed text and attachments with no Enter instruction");
        plan = DeliveryPlan.Build(phrase, DeliveryMode.Send);
        check(plan.Count == 3 && plan.All(s => s.PressEnter) && plan[0].Text == "前文" && plan[1].AttachmentIndices.Single() == 0 && plan[2].Text == "后文", "arrow send mode retains the exact text-image-text sequence and explicit confirmation");
        phrase.Text = "#图1#";
        check(DeliveryPlan.Build(phrase, DeliveryMode.PasteOnly).Count == 1 && !DeliveryPlan.Build(phrase, DeliveryMode.PasteOnly)[0].PressEnter, "image-only paste mode does not create an empty text step or an Enter instruction");
        phrase.Attachments.Clear(); phrase.InlineAttachments = false; phrase.Text = "真的很实惠\r\n下一行";
        plan = DeliveryPlan.Build(phrase, DeliveryMode.PasteOnly);
        check(plan.Count == 1 && plan[0].Text == phrase.Text && !plan[0].PressEnter && plan[0].AttachmentIndices.Length == 0, "text-only double click preserves original text and newlines while leaving sending to the user");

        Boards(Path.Combine(root, "boards"), check);
        Migration(Path.Combine(root, "migration"), check);
        Transfer(Path.Combine(root, "board-transfer"), check);
    }

    private static void Boards(string root, Action<bool, string> check)
    {
        LocalStore store = new LocalStore(root);
        check(store.Data.Boards.Select(b => b.Name).SequenceEqual(new[] { "公共", "个人" }) && store.Data.Categories.All(c => c.BoardId == Board.PersonalId), "default public and personal boards keep existing-style categories under personal");
        Board custom = store.AddBoard("部门"); Category publicCommon = store.AddCategory("常用", Board.PublicId), customCommon = store.AddCategory("常用", custom.Id);
        Category movable = store.AddCategory("可移动", custom.Id);
        store.Data.Phrases.Add(new Phrase { Id = "public", CategoryId = publicCommon.Id, Keyword = "公共", Text = "相同内容" });
        store.Data.Phrases.Add(new Phrase { Id = "custom", CategoryId = customCommon.Id, Keyword = "部门", Text = "相同内容" });
        store.Data.Phrases.Add(new Phrase { Id = "move", CategoryId = movable.Id, Keyword = "移动", Text = "移动正文" }); store.Save();
        check(store.Search("", "相同内容", Board.PublicId).Single().Id == "public" && store.Search("", "相同内容", custom.Id).Single().Id == "custom" && store.Data.Categories.Count(c => c.Name == "常用") == 3,
            "identically named categories in different boards remain separate and board searches do not leak phrases");
        Reject(delegate { store.AddCategory("常用", custom.Id); }, "duplicate category names are still rejected within a single board", check);
        Reject(delegate { store.AddBoard("部门"); }, "duplicate board names are rejected", check);
        Reject(delegate { store.RenameBoard(Board.PublicId, "改名"); }, "built-in public board cannot be renamed", check);
        Reject(delegate { store.DeleteBoard(Board.PersonalId); }, "built-in personal board cannot be removed", check);
        Reject(delegate { store.MoveCategoryToBoard(customCommon.Id, Board.PublicId); }, "moving a category into an existing same-name category is rejected without overwriting content", check);
        store.MoveCategoryToBoard(movable.Id, Board.PublicId);
        check(store.Data.Phrases.First(p => p.Id == "move").CategoryId == movable.Id && movable.BoardId == Board.PublicId && store.Search("", "移动正文", Board.PublicId).Count == 1,
            "moving a whole category changes board membership without changing its phrases or IDs");
        store.RenameBoard(custom.Id, "售后组"); store.Data.Preferences.ActiveBoardId = custom.Id; store.Data.Preferences.CategoryPanelHeight = 230; store.Save();
        LocalStore restarted = new LocalStore(root);
        check(restarted.Data.Boards.Any(b => b.Id == custom.Id && b.Name == "售后组") && restarted.Data.Categories.First(c => c.Id == customCommon.Id).BoardId == custom.Id && restarted.Data.Preferences.ActiveBoardId == custom.Id && restarted.Data.Preferences.CategoryPanelHeight == 230,
            "board names, membership, selected board and category-panel height survive restart");
        byte[] disk = File.ReadAllBytes(store.FilePath); Directory.CreateDirectory(store.FilePath + ".tmp");
        Reject(delegate { store.MoveCategoryToBoard(movable.Id, custom.Id); }, "category board moves report failed saves", check);
        Reject(delegate { store.RenameBoard(custom.Id, "失败改名"); }, "board renaming reports failed saves", check);
        Reject(delegate { store.AddBoard("失败新增"); }, "board creation reports failed saves", check);
        Reject(delegate { store.DeleteBoard(custom.Id); }, "board deletion reports failed saves", check);
        check(movable.BoardId == Board.PublicId && custom.Name == "售后组" && store.Data.Boards.Count == 3 && store.Data.Categories.Contains(customCommon) && customCommon.BoardId == custom.Id && store.Data.Phrases.First(p => p.Id == "custom").CategoryId == customCommon.Id && disk.SequenceEqual(File.ReadAllBytes(store.FilePath)),
            "failed board edits restore names, category membership, merged phrase references and on-disk data");
        Directory.Delete(store.FilePath + ".tmp");
        Category extra = store.AddCategory("独有分类", custom.Id);
        store.Data.Phrases.Add(new Phrase { Id = "unique", CategoryId = extra.Id, Keyword = "", Text = "保留我" }); store.Save();
        string personalCommon = store.Data.Categories.First(c => c.BoardId == Board.PersonalId && c.Name == "常用").Id; int count = store.Data.Phrases.Count;
        store.DeleteBoard(custom.Id);
        check(store.Data.Phrases.Count == count && !store.Data.Boards.Any(b => b.Id == custom.Id) && store.Data.Phrases.First(p => p.Id == "custom").CategoryId == personalCommon && extra.BoardId == Board.PersonalId && store.Data.Phrases.First(p => p.Id == "unique").CategoryId == extra.Id,
            "deleting a custom board preserves every phrase, merges personal same-name categories and moves unique categories intact");
        Reject(delegate { store.DeleteCategory(publicCommon.Id); store.DeleteCategory(movable.Id); }, "deleting a board's final nonempty category cannot send its phrases into another board", check);
    }

    private static readonly string Legacy = "{\"Version\":1,\"Categories\":[{\"Id\":\"old-category\",\"Name\":\"旧分类\"}],\"Phrases\":[{\"Id\":\"old-phrase\",\"CategoryId\":\"old-category\",\"Keyword\":\"旧关键词\",\"ColorHex\":\"#9329AE\",\"Text\":\"旧正文\\r\\n第二行\",\"Attachments\":[]}],\"Preferences\":{\"TopMost\":true,\"Locked\":true,\"AutoSend\":true,\"X\":88,\"Y\":99,\"Width\":370,\"Height\":680}}";

    private static void Migration(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root); string data = Path.Combine(root, "话术.json"); File.WriteAllText(data, Legacy, new UTF8Encoding(false)); byte[] original = File.ReadAllBytes(data);
        LocalStore migrated = new LocalStore(root);
        check(migrated.Data.Version == 2 && migrated.Data.Boards.Count == 2 && migrated.Data.Categories[0].BoardId == Board.PersonalId && migrated.Data.Categories[0].Id == "old-category" && migrated.Data.Phrases[0].Text == "旧正文\r\n第二行" && migrated.Data.Preferences.Locked && migrated.Data.Preferences.X == 88,
            "v1 local data upgrades automatically into personal without changing phrase content, IDs or window settings");
        check(Directory.GetFiles(root, "话术.板块升级前-*.json").Length == 1 && File.ReadAllBytes(Directory.GetFiles(root, "话术.板块升级前-*.json").Single()).SequenceEqual(original) && File.ReadAllText(data).Contains("\"Boards\""),
            "board migration retains the exact pre-upgrade JSON and persists the upgraded format atomically");
        new LocalStore(root);
        check(Directory.GetFiles(root, "话术.板块升级前-*.json").Length == 1, "restarting upgraded data does not rerun or duplicate migration");
        string recovery = Path.Combine(root, "recovery"); Directory.CreateDirectory(recovery);
        File.WriteAllText(Path.Combine(recovery, "话术.json"), "damaged"); File.WriteAllText(Path.Combine(recovery, "话术.json.bak"), Legacy, new UTF8Encoding(false));
        LocalStore recovered = new LocalStore(recovery);
        check(recovered.RecoveredBackup && recovered.Data.Version == 2 && File.ReadAllBytes(Directory.GetFiles(recovery, "话术.板块升级前-*.json").Single()).SequenceEqual(original),
            "recovery from an old-format backup upgrades safely while preserving the actual legacy bytes");
    }

    private static void Transfer(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        for (int version = 1; version <= 2; version++)
        {
            string pack = Path.Combine(root, "legacy-" + version + ".quickreply"); WriteLegacyPackage(pack, version);
            using (ImportedLibrary incoming = LibraryTransfer.Load(pack))
            {
                check(!incoming.HasBoardLayout && incoming.Library.Phrases[0].Text == "旧正文\r\n第二行", "old v" + version + " quickreply packages remain readable after board upgrade");
                LocalStore target = new LocalStore(Path.Combine(root, "legacy-target-" + version));
                ImportResult first = LibraryTransfer.Import(target, incoming, false, true, Board.PublicId);
                string id = target.Data.Phrases.First(p => p.Keyword == "旧关键词").CategoryId;
                check(first.Added == 1 && target.Data.Categories.First(c => c.Id == id).BoardId == Board.PublicId && target.Search("", "旧正文", Board.PersonalId).Count == 0,
                    "old v" + version + " package imports into the chosen public board without affecting personal content");
                check(LibraryTransfer.Import(target, incoming, false, true, Board.PublicId).Skipped == 1 && LibraryTransfer.Import(target, incoming, false, true, Board.PersonalId).Added == 1,
                    "legacy duplicate skipping is scoped to the selected destination board");
            }
        }
        string csv = Path.Combine(root, "old.csv"); File.WriteAllText(csv, "分类,关键词,话术内容\r\n常用,旧表格,旧表格正文\r\n", new UTF8Encoding(true));
        LegacyInlinePackage(Path.Combine(root, "old-images.quickreply"), check);
        LocalStore source = new LocalStore(Path.Combine(root, "source")); Category publicCommon = source.AddCategory("常用", Board.PublicId); Board custom = source.AddBoard("部门"); Category customCategory = source.AddCategory("资料", custom.Id);
        using (ImportedLibrary table = LibraryTransfer.Load(csv))
            check(!table.HasBoardLayout && LibraryTransfer.Import(source, table, false, true, Board.PublicId).Added == 1 && source.Search("", "旧表格正文", Board.PublicId).Count == 1,
                "old CSV tables also import into the chosen board and merge its same-name categories");
        source.Data.Phrases.Add(new Phrase { Id = "custom", CategoryId = customCategory.Id, Keyword = "资料", Text = "部门资料" }); source.Save();
        string all = Path.Combine(root, "all.quickreply"), boardOnly = Path.Combine(root, "public.quickreply"), categoryOnly = Path.Combine(root, "category.quickreply");
        LibraryTransfer.ExportPackage(source, all, ""); LibraryTransfer.ExportPackage(source, boardOnly, "", Board.PublicId); LibraryTransfer.ExportPackage(source, categoryOnly, customCategory.Id);
        using (ImportedLibrary incoming = LibraryTransfer.Load(all))
        {
            check(incoming.HasBoardLayout && incoming.Library.Boards.Count == 3 && incoming.Library.Categories.Count(c => c.Name == "常用") == 2 && incoming.Library.Phrases.Count == source.Data.Phrases.Count,
                "v3 packages preserve custom boards, same-name categories and every phrase");
            LocalStore target = new LocalStore(Path.Combine(root, "target")); target.AddBoard("部门"); target.Data.Preferences.CategoryPanelHeight = 222; Preferences preferences = target.Data.Preferences;
            ImportResult added = LibraryTransfer.Import(target, incoming, false, true);
            check(added.Added == 2 && target.Data.Boards.Count(b => b.Name == "部门") == 1 && target.Search("", "旧表格正文", Board.PublicId).Count == 1 && target.Data.Preferences == preferences && target.Data.Preferences.CategoryPanelHeight == 222,
                "board package append merges boards by name, scopes duplicates and retains local view preferences");
            check(LibraryTransfer.Import(target, incoming, false, true).Added == 0, "reimport of a multi-board package skips duplicates in each board");
            LibraryTransfer.Import(target, incoming, true, true);
            check(target.Data.Boards.Select(b => b.Name).SequenceEqual(source.Data.Boards.Select(b => b.Name)) && target.Data.Phrases.Select(p => p.Text).SequenceEqual(source.Data.Phrases.Select(p => p.Text)),
                "replace import restores the board and phrase order of the exported package");
            byte[] disk = File.ReadAllBytes(target.FilePath); Directory.CreateDirectory(target.FilePath + ".tmp");
            string boardsBefore = String.Join("|", target.Data.Boards.Select(b => b.Id + ":" + b.Name));
            Reject(delegate { LibraryTransfer.Import(target, incoming, true, true); }, "multi-board import reports failed saves", check);
            check(boardsBefore == String.Join("|", target.Data.Boards.Select(b => b.Id + ":" + b.Name)) && disk.SequenceEqual(File.ReadAllBytes(target.FilePath)), "failed package import rolls back board IDs along with existing data");
            Directory.Delete(target.FilePath + ".tmp");
        }
        using (ImportedLibrary incoming = LibraryTransfer.Load(boardOnly)) check(incoming.Library.Categories.Count == 1 && incoming.Library.Categories[0].BoardId == Board.PublicId && incoming.Library.Phrases.Count == 1, "current-board export includes only that board's categories and phrases");
        using (ImportedLibrary incoming = LibraryTransfer.Load(categoryOnly)) check(incoming.Library.Categories.Count == 1 && incoming.Library.Boards.Any(b => b.Name == "部门") && incoming.Library.Phrases.Single().Text == "部门资料", "category export includes its board definition and excludes unrelated phrases");
        LibraryTransfer.ExportCsv(source, csv, "");
        using (ImportedLibrary incoming = LibraryTransfer.Load(csv)) check(incoming.HasBoardLayout && incoming.Library.Categories.Count(c => c.Name == "常用") == 2 && incoming.Library.Boards.Any(b => b.Name == "部门") && incoming.Library.Phrases.Count == source.Data.Phrases.Count,
            "new CSV exports retain board names and same-name categories on round trip");
        string invalid = Path.Combine(root, "invalid-v3.quickreply"); WriteLegacyPackage(invalid, 3);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(invalid)) { } }, "v3 packages cannot omit the board schema required by their declared version", check);
    }

    private static void WriteLegacyPackage(string path, int version)
    {
        using (FileStream file = File.Create(path)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create)) using (Stream stream = zip.CreateEntry("快答话术包.json").Open())
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"Format\":\"QuickReplyLite.PhrasePack\",\"Version\":" + version + ",\"Library\":" + Legacy + ",\"Files\":[]}"); stream.Write(json, 0, json.Length);
        }
    }

    private static void LegacyInlinePackage(string path, Action<bool, string> check)
    {
        byte[] image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQ0AAAAASUVORK5CYII=");
        string library = Legacy.Replace("旧正文\\r\\n第二行", "前文#图1#后文").Replace("\"Attachments\":[]", "\"Attachments\":[{\"Name\":\"旧图片.png\",\"RelativePath\":\"素材/old.png\"}],\"InlineAttachments\":true");
        string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(image)).Replace("-", "").ToLowerInvariant();
        string manifest = "{\"Format\":\"QuickReplyLite.PhrasePack\",\"Version\":2,\"Library\":" + library + ",\"Files\":[{\"Path\":\"素材/old.png\",\"Size\":" + image.Length + ",\"Sha256\":\"" + hash + "\"}]}";
        using (FileStream file = File.Create(path)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using (Stream stream = zip.CreateEntry("快答话术包.json").Open()) { byte[] json = Encoding.UTF8.GetBytes(manifest); stream.Write(json, 0, json.Length); }
            using (Stream stream = zip.CreateEntry("素材/old.png").Open()) stream.Write(image, 0, image.Length);
        }
        using (ImportedLibrary incoming = LibraryTransfer.Load(path))
        {
            Phrase phrase = incoming.Library.Phrases[0];
            check(!incoming.HasBoardLayout && phrase.InlineAttachments && PhraseContent.Build(phrase).Count == 3 && File.ReadAllBytes(incoming.Sources["素材/old.png"]).SequenceEqual(image),
                "old v2 image packages retain attachment bytes and inline image positions during board migration");
            LocalStore target = new LocalStore(Path.Combine(Path.GetDirectoryName(path), "old-image-target")); LibraryTransfer.Import(target, incoming, false, true, Board.PublicId);
            Phrase imported = target.Search("", "旧关键词", Board.PublicId).Single();
            check(imported.Text == "前文#图1#后文" && File.ReadAllBytes(target.Resolve(imported.Attachments.Single())).SequenceEqual(image) && DeliveryPlan.Build(imported, DeliveryMode.PasteOnly).All(s => !s.PressEnter),
                "old mixed-image packages import into public and remain paste-only when double clicked");
        }
    }
    private static void Reject(Action action, string name, Action<bool, string> check) { bool failed = false; try { action(); } catch { failed = true; } check(failed, name); }
}
