using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickReplyLite
{
    [DataContract]
    public sealed class Board
    {
        public const string PublicId = "public", PersonalId = "personal";
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string Name { get; set; }
        public static List<Board> Defaults() { return new List<Board> { new Board { Id = PublicId, Name = "公共" }, new Board { Id = PersonalId, Name = "个人" } }; }
    }

    [DataContract]
    public sealed class Category
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string BoardId { get; set; }
        public Category() { BoardId = Board.PersonalId; }
    }

    [DataContract]
    public sealed class Attachment
    {
        [DataMember(Order = 0)] public string Name { get; set; }
        [DataMember(Order = 1)] public string RelativePath { get; set; }
    }

    [DataContract]
    public sealed class Phrase
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string CategoryId { get; set; }
        [DataMember(Order = 2)] public string Keyword { get; set; }
        [DataMember(Order = 3)] public string ColorHex { get; set; }
        [DataMember(Order = 4)] public string Text { get; set; }
        [DataMember(Order = 5)] public List<Attachment> Attachments { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public bool InlineAttachments { get; set; }
        public Phrase() { Attachments = new List<Attachment>(); }
        public override string ToString() { return (Keyword ?? "") + " " + LocalStore.Preview(this); }
    }

    public sealed class SendPart
    {
        public string Text { get; internal set; }
        public int AttachmentIndex { get; internal set; }
    }

    public static class PhraseContent
    {
        internal static readonly Regex Marker = new Regex(@"#(?<kind>图|图片|文件|附件)(?<index>[0-9]+)#", RegexOptions.CultureInvariant);
        public static string PlainText(Phrase phrase) { return phrase.InlineAttachments ? Marker.Replace(phrase.Text, "") : phrase.Text; }
        public static List<SendPart> Build(Phrase phrase)
        {
            List<SendPart> result = new List<SendPart>(); HashSet<int> used = new HashSet<int>();
            if (!phrase.InlineAttachments)
            {
                if (!String.IsNullOrWhiteSpace(phrase.Text)) result.Add(new SendPart { Text = phrase.Text, AttachmentIndex = -1 });
            }
            else
            {
                int start = 0;
                foreach (Match match in Marker.Matches(phrase.Text))
                {
                    int index;
                    if (!Int32.TryParse(match.Groups["index"].Value, out index) || index < 1 || index > phrase.Attachments.Count) throw new InvalidDataException("图片/文件标记没有对应附件，请检查 #图1# 等标记。");
                    string text = phrase.Text.Substring(start, match.Index - start);
                    if (!String.IsNullOrWhiteSpace(text)) result.Add(new SendPart { Text = text, AttachmentIndex = -1 });
                    result.Add(new SendPart { AttachmentIndex = index - 1 }); used.Add(index - 1); start = match.Index + match.Length;
                    if (result.Count > 64) throw new InvalidDataException("单条话术的图文分段过多。");
                }
                string tail = phrase.Text.Substring(start); if (!String.IsNullOrWhiteSpace(tail)) result.Add(new SendPart { Text = tail, AttachmentIndex = -1 });
            }
            for (int i = 0; i < phrase.Attachments.Count; i++) if (!used.Contains(i)) result.Add(new SendPart { AttachmentIndex = i });
            if (result.Count > 64) throw new InvalidDataException("单条话术的图文分段过多，最多64步。");
            return result;
        }
        public static string PlaceAttachment(string text, int position, int oneBasedIndex, bool image, out int caret)
        {
            if (text == null) throw new ArgumentNullException("text");
            if (position < 0 || position > text.Length || oneBasedIndex < 1 || oneBasedIndex > 9) throw new ArgumentOutOfRangeException();
            foreach (Match match in Marker.Matches(text))
                if (position > match.Index && position < match.Index + match.Length) { position = match.Index + match.Length; break; }
            int adjusted = position;
            string remaining = Marker.Replace(text, delegate(Match match)
            {
                int index;
                if (!Int32.TryParse(match.Groups["index"].Value, out index) || index != oneBasedIndex) return match.Value;
                if (match.Index < position) adjusted -= Math.Min(match.Length, position - match.Index);
                return "";
            });
            string marker = "#" + (image ? "图" : "文件") + oneBasedIndex + "#";
            caret = adjusted + marker.Length;
            return remaining.Insert(adjusted, marker);
        }
        public static string RemoveAttachmentMarker(string text, int oneBasedIndex)
        {
            return Marker.Replace(text, delegate(Match match)
            {
                int index; if (!Int32.TryParse(match.Groups["index"].Value, out index)) return match.Value;
                return index == oneBasedIndex ? "" : index > oneBasedIndex ? "#" + match.Groups["kind"].Value + (index - 1) + "#" : match.Value;
            });
        }
    }

    public sealed class TextReplacement
    {
        public string PhraseId { get; private set; }
        public string Before { get; private set; }
        public string After { get; private set; }
        public int Occurrences { get; private set; }
        internal TextReplacement(string id, string before, string after, int occurrences)
        { PhraseId = id; Before = before; After = after; Occurrences = occurrences; }
    }

    [DataContract]
    public sealed class Preferences
    {
        [DataMember(Order = 0)] public bool TopMost { get; set; }
        [DataMember(Order = 1)] public bool Locked { get; set; }
        [DataMember(Order = 2)] public bool AutoSend { get; set; }
        [DataMember(Order = 3)] public int X { get; set; }
        [DataMember(Order = 4)] public int Y { get; set; }
        [DataMember(Order = 5)] public int Width { get; set; }
        [DataMember(Order = 6)] public int Height { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string ActiveBoardId { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public int CategoryPanelHeight { get; set; }
        public Preferences() { TopMost = true; AutoSend = true; Width = 370; Height = 680; X = -1; Y = -1; ActiveBoardId = Board.PersonalId; CategoryPanelHeight = 145; }
    }

    [DataContract]
    public sealed class Document
    {
        [DataMember(Order = 0)] public int Version { get; set; }
        [DataMember(Order = 1)] public List<Category> Categories { get; set; }
        [DataMember(Order = 2)] public List<Phrase> Phrases { get; set; }
        [DataMember(Order = 3)] public Preferences Preferences { get; set; }
        [DataMember(Order = 4)] public List<Board> Boards { get; set; }
        internal bool Migrated;
        public Document() { Version = 2; Categories = new List<Category>(); Phrases = new List<Phrase>(); Preferences = new Preferences(); Boards = Board.Defaults(); }
        [OnDeserialized] private void Upgrade(StreamingContext context)
        {
            if (Version != 1 || Boards != null) return;
            Boards = Board.Defaults();
            if (Categories != null) foreach (Category category in Categories) if (category != null) category.BoardId = Board.PersonalId;
            Version = 2; Migrated = true;
        }
    }

    public sealed class LocalStore
    {
        public string Folder { get; private set; }
        public string FilePath { get { return Path.Combine(Folder, "话术.json"); } }
        public Document Data { get; private set; }
        public bool RecoveredBackup { get; private set; }

        public LocalStore(string folder)
        {
            Folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Path.Combine(Folder, "素材"));
            if (File.Exists(FilePath))
            {
                try { Data = Read(FilePath); Validate(Data); }
                catch
                {
                    string backup = FilePath + ".bak";
                    if (!File.Exists(backup)) throw new InvalidDataException("话术文件无法读取。请保留原文件，并检查备份：" + FilePath);
                    try { Data = Read(backup); Validate(Data); }
                    catch { throw new InvalidDataException("话术文件及备份均无法读取，请先保留数据文件夹。" + Folder); }
                    File.Copy(FilePath, FilePath + ".损坏-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), true);
                    RecoveredBackup = true;
                    // Preserve the good backup while recovering a damaged primary file.
                    if (Data.Migrated) File.Copy(backup, Path.Combine(Folder, "话术.板块升级前-" + Guid.NewGuid().ToString("N") + ".json"), false);
                    File.Delete(FilePath);
                    Save(); Data.Migrated = false;
                }
                if (Data.Migrated)
                {
                    File.Copy(FilePath, Path.Combine(Folder, "话术.板块升级前-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), false);
                    Save(); Data.Migrated = false;
                }
            }
            else { Data = Seed(); Save(); }
        }

        public static Document Read(string file)
        {
            if (new FileInfo(file).Length > 20 * 1024 * 1024) throw new InvalidDataException("话术文件过大。");
            using (FileStream stream = File.OpenRead(file))
                return (Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
        }

        public static void Validate(Document doc)
        {
            if (doc == null || doc.Version != 2 || doc.Categories == null || doc.Phrases == null || doc.Preferences == null || doc.Boards == null)
                throw new InvalidDataException("话术文件格式不正确。");
            if (doc.Categories.Count == 0 || doc.Categories.Count > 200 || doc.Phrases.Count > 10000)
                throw new InvalidDataException("分类或话术数量无效。");
            if (doc.Boards.Count < 2 || doc.Boards.Count > 50) throw new InvalidDataException("板块数量无效，最多支持50个板块。");
            HashSet<string> boardIds = new HashSet<string>(), boardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Board board in doc.Boards)
                if (board == null || String.IsNullOrWhiteSpace(board.Id) || !boardIds.Add(board.Id) || String.IsNullOrWhiteSpace(board.Name) || board.Name.Length > 30 || !boardNames.Add(board.Name.Trim())) throw new InvalidDataException("板块名称或编号无效。");
            if (!doc.Boards.Any(b => b.Id == Board.PublicId && b.Name == "公共") || !doc.Boards.Any(b => b.Id == Board.PersonalId && b.Name == "个人")) throw new InvalidDataException("缺少公共或个人板块。");
            HashSet<string> categoryIds = new HashSet<string>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Category category in doc.Categories)
            {
                if (category == null || String.IsNullOrWhiteSpace(category.Id) || !categoryIds.Add(category.Id) ||
                    String.IsNullOrWhiteSpace(category.Name) || category.Name.Length > 30 || !boardIds.Contains(category.BoardId ?? "") || !names.Add(category.BoardId + "\n" + category.Name.Trim()))
                    throw new InvalidDataException("分类名称或编号无效。");
            }
            HashSet<string> phraseIds = new HashSet<string>();
            foreach (Phrase phrase in doc.Phrases)
            {
                if (phrase == null || String.IsNullOrEmpty(phrase.Id) || !phraseIds.Add(phrase.Id) ||
                    !categoryIds.Contains(phrase.CategoryId) || phrase.Text == null || phrase.Text.Length > 50000 ||
                    phrase.Keyword == null || phrase.Keyword.Length > 80 || phrase.Attachments == null || phrase.Attachments.Count > 9)
                    throw new InvalidDataException("话术格式无效。");
                if (String.IsNullOrWhiteSpace(phrase.Text) && phrase.Attachments.Count == 0)
                    throw new InvalidDataException("每条话术至少需要文字或附件。");
                foreach (Attachment attachment in phrase.Attachments)
                {
                    if (attachment == null || String.IsNullOrWhiteSpace(attachment.Name) || String.IsNullOrWhiteSpace(attachment.RelativePath))
                        throw new InvalidDataException("附件格式无效。");
                    ValidateRelativePath(attachment.RelativePath);
                }
                if (phrase.InlineAttachments) PhraseContent.Build(phrase);
            }
        }

        private static void ValidateRelativePath(string value)
        {
            string normalized = value.Replace('\\', '/');
            if (Path.IsPathRooted(value) || normalized.StartsWith("/") || normalized.Contains(":") ||
                normalized.Split('/').Any(part => part == "..") || !normalized.StartsWith("素材/", StringComparison.Ordinal))
                throw new InvalidDataException("附件路径必须在本地素材文件夹中。");
        }

        public string Resolve(Attachment attachment)
        {
            ValidateRelativePath(attachment.RelativePath);
            string full = Path.GetFullPath(Path.Combine(Folder, attachment.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("附件路径无效。");
            return full;
        }

        public Attachment CopyAttachment(string source)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("文件不存在：" + Path.GetFileName(source));
            string name = Path.GetFileName(source);
            string relative = "素材/" + Guid.NewGuid().ToString("N") + "/" + name;
            string target = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, false);
            return new Attachment { Name = name, RelativePath = relative };
        }

        public void Save()
        {
            Validate(Data);
            string temp = FilePath + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (System.Xml.XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
                { new DataContractJsonSerializer(typeof(Document)).WriteObject(writer, Data); writer.Flush(); }
                stream.Flush(true);
                if (stream.Length > 20 * 1024 * 1024) throw new InvalidDataException("话术文件超过20 MB，请精简文字后再保存。");
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
        }

        public List<Phrase> Search(string categoryId, string query, string boardId = null)
        {
            string value = (query ?? "").Trim();
            HashSet<string> categories = new HashSet<string>(Data.Categories.Where(c => String.IsNullOrEmpty(boardId) || c.BoardId == boardId).Select(c => c.Id));
            return Data.Phrases.Where(p => categories.Contains(p.CategoryId) && (String.IsNullOrEmpty(categoryId) || p.CategoryId == categoryId) &&
                (value.Length == 0 || (p.Keyword + "\n" + p.Text + "\n" + String.Join("\n", p.Attachments.Select(a => a.Name)))
                    .IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        public List<TextReplacement> PreviewTextReplacement(string find, string replacement)
        {
            if (String.IsNullOrEmpty(find)) throw new InvalidOperationException("请输入要查找的文字。");
            replacement = replacement ?? "";
            List<TextReplacement> result = new List<TextReplacement>();
            foreach (Phrase phrase in Data.Phrases)
            {
                int count = 0, position = 0, found;
                while ((found = phrase.Text.IndexOf(find, position, StringComparison.Ordinal)) >= 0)
                { count++; position = found + find.Length; }
                if (count > 0)
                {
                    long length = phrase.Text.Length + (long)count * (replacement.Length - find.Length);
                    if (length > 50000) throw new InvalidOperationException("替换后有话术超过50000字，请缩短替换文字。");
                    result.Add(new TextReplacement(phrase.Id, phrase.Text, phrase.Text.Replace(find, replacement), count));
                }
            }
            return result;
        }

        public int ApplyTextReplacements(IList<TextReplacement> replacements)
        {
            if (replacements == null) throw new ArgumentNullException("replacements");
            Dictionary<string, Phrase> phrases = Data.Phrases.ToDictionary(p => p.Id);
            HashSet<string> seen = new HashSet<string>();
            List<TextReplacement> changed = new List<TextReplacement>();
            foreach (TextReplacement item in replacements)
            {
                Phrase phrase;
                if (item == null || !seen.Add(item.PhraseId) || !phrases.TryGetValue(item.PhraseId, out phrase) || phrase.Text != item.Before)
                    throw new InvalidOperationException("话术已发生变化，请重新查找并预览。");
                if (item.Before != item.After) changed.Add(item);
            }
            if (changed.Count == 0) return 0;
            foreach (TextReplacement item in changed) phrases[item.PhraseId].Text = item.After;
            try
            {
                Validate(Data);
                string backup = Path.Combine(Folder, "话术.批量替换前-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
                File.Copy(FilePath, backup, false);
                Save();
            }
            catch
            {
                foreach (TextReplacement item in changed) phrases[item.PhraseId].Text = item.Before;
                throw;
            }
            return changed.Count;
        }

        public bool MovePhrase(string sourceId, string targetId, bool after)
        {
            Phrase source = Data.Phrases.FirstOrDefault(p => p.Id == sourceId);
            Phrase target = Data.Phrases.FirstOrDefault(p => p.Id == targetId);
            if (source == null || target == null) throw new InvalidOperationException("话术已变化，请重新拖动。");
            if (source == target) return false;
            List<Phrase> before = Data.Phrases.ToList();
            Data.Phrases.Remove(source);
            Data.Phrases.Insert(Data.Phrases.IndexOf(target) + (after ? 1 : 0), source);
            if (before.SequenceEqual(Data.Phrases)) return false;
            try { Save(); }
            catch { Data.Phrases.Clear(); Data.Phrases.AddRange(before); throw; }
            return true;
        }

        public Category AddCategory(string name, string boardId = Board.PersonalId)
        {
            if (Data.Categories.Count >= 200) throw new InvalidOperationException("最多支持200个分类。");
            name = (name ?? "").Trim();
            if (!Data.Boards.Any(b => b.Id == boardId)) throw new InvalidOperationException("请选择存在的板块。");
            if (name.Length == 0 || name.Length > 30 || Data.Categories.Any(c => c.BoardId == boardId && String.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("请输入不重复的分类名称（最多30字）。");
            Category result = new Category { Id = Guid.NewGuid().ToString("N"), Name = name, BoardId = boardId };
            Data.Categories.Add(result);
            try { Save(); } catch { Data.Categories.Remove(result); throw; }
            return result;
        }

        public void DeleteCategory(string id)
        {
            if (Data.Categories.Count <= 1) throw new InvalidOperationException("至少保留一个分类。");
            Category target = Data.Categories.FirstOrDefault(c => c.Id == id);
            if (target == null) return;
            Category next = Data.Categories.FirstOrDefault(c => c.Id != id && c.BoardId == target.BoardId);
            if (next == null && Data.Phrases.Any(p => p.CategoryId == id)) throw new InvalidOperationException("当前板块只有这个分类，请先新建另一个分类，再删除以接收话术。");
            string destination = next == null ? Data.Categories.First(c => c.Id != id).Id : next.Id;
            int index = Data.Categories.IndexOf(target);
            List<Phrase> moved = Data.Phrases.Where(p => p.CategoryId == id).ToList();
            foreach (Phrase phrase in moved) phrase.CategoryId = destination;
            Data.Categories.Remove(target);
            try { Save(); }
            catch
            {
                Data.Categories.Insert(index, target);
                foreach (Phrase phrase in moved) phrase.CategoryId = id;
                throw;
            }
        }

        public Board AddBoard(string name)
        {
            name = (name ?? "").Trim();
            if (Data.Boards.Count >= 50 || name.Length == 0 || name.Length > 30 || Data.Boards.Any(b => String.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("请输入不重复的板块名称（最多30字，最多50个板块）。");
            Board board = new Board { Id = Guid.NewGuid().ToString("N"), Name = name }; Data.Boards.Add(board);
            try { Save(); } catch { Data.Boards.Remove(board); throw; } return board;
        }

        public void RenameBoard(string id, string name)
        {
            Board board = Data.Boards.FirstOrDefault(b => b.Id == id); name = (name ?? "").Trim();
            if (board == null || id == Board.PublicId || id == Board.PersonalId) throw new InvalidOperationException("公共和个人板块保留，自定义板块可以重命名。");
            if (name.Length == 0 || name.Length > 30 || Data.Boards.Any(b => b.Id != id && String.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("请输入不重复的板块名称（最多30字）。");
            string old = board.Name; board.Name = name; try { Save(); } catch { board.Name = old; throw; }
        }

        public void MoveCategoryToBoard(string id, string boardId)
        {
            Category category = Data.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null || !Data.Boards.Any(b => b.Id == boardId)) throw new InvalidOperationException("分类或板块已变化，请重试。");
            if (Data.Categories.Any(c => c.Id != id && c.BoardId == boardId && String.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("目标板块已有同名分类，请先重命名当前分类。");
            string old = category.BoardId; if (old == boardId) return; category.BoardId = boardId;
            try { Save(); } catch { category.BoardId = old; throw; }
        }

        public void DeleteBoard(string id)
        {
            if (id == Board.PublicId || id == Board.PersonalId) throw new InvalidOperationException("公共和个人板块不能删除。");
            Board board = Data.Boards.FirstOrDefault(b => b.Id == id); if (board == null) return;
            int index = Data.Boards.IndexOf(board); List<Category> oldCategories = Data.Categories.ToList();
            Dictionary<string, string> oldBoards = oldCategories.ToDictionary(c => c.Id, c => c.BoardId), oldPhraseCategories = Data.Phrases.ToDictionary(p => p.Id, p => p.CategoryId);
            foreach (Category category in oldCategories.Where(c => c.BoardId == id))
            {
                Category same = Data.Categories.FirstOrDefault(c => c.BoardId == Board.PersonalId && String.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase));
                if (same == null) category.BoardId = Board.PersonalId;
                else { foreach (Phrase phrase in Data.Phrases.Where(p => p.CategoryId == category.Id)) phrase.CategoryId = same.Id; Data.Categories.Remove(category); }
            }
            Data.Boards.Remove(board);
            try { Save(); }
            catch
            {
                Data.Boards.Insert(index, board); Data.Categories.Clear(); Data.Categories.AddRange(oldCategories);
                foreach (Category category in Data.Categories) category.BoardId = oldBoards[category.Id];
                foreach (Phrase phrase in Data.Phrases) phrase.CategoryId = oldPhraseCategories[phrase.Id]; throw;
            }
        }

        public string CategoryLabel(Category category) { return Data.Boards.First(b => b.Id == category.BoardId).Name + " / " + category.Name; }

        public static string Preview(Phrase phrase)
        {
            if (!String.IsNullOrWhiteSpace(phrase.Text)) return phrase.Text.Replace("\r", " ").Replace("\n", " ");
            return String.Join("、", phrase.Attachments.Select(a => a.Name));
        }

        private static Document Seed()
        {
            Document doc = new Document();
            Category category = new Category { Id = Guid.NewGuid().ToString("N"), Name = "常用" };
            doc.Categories.Add(category);
            string[] labels = { "问候", "稍等", "感谢" };
            string[] texts = { "您好，请问有什么可以帮您？", "正在为您核实，请稍等片刻。", "好的，感谢您的配合！" };
            for (int i = 0; i < texts.Length; i++) doc.Phrases.Add(new Phrase { Id = Guid.NewGuid().ToString("N"), CategoryId = category.Id, Keyword = labels[i], ColorHex = "#2878D0", Text = texts[i] });
            return doc;
        }
    }
}
