using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace QuickReplyLite
{
    [DataContract]
    public sealed class LibraryPackage
    {
        [DataMember(Order = 0)] public string Format { get; set; }
        [DataMember(Order = 1)] public int Version { get; set; }
        [DataMember(Order = 2)] public Document Library { get; set; }
        [DataMember(Order = 3)] public List<PackageFile> Files { get; set; }
    }

    [DataContract]
    public sealed class PackageFile
    {
        [DataMember(Order = 0)] public string Path { get; set; }
        [DataMember(Order = 1)] public long Size { get; set; }
        [DataMember(Order = 2)] public string Sha256 { get; set; }
    }

    public sealed class ImportWarning
    {
        public string PhraseId { get; internal set; }
        public string Description { get; internal set; }
        public string OriginalText { get; internal set; }
        public string Keyword { get; internal set; }
        public string OriginalReferences { get; internal set; }
    }

    public sealed class ImportedLibrary : IDisposable
    {
        public Document Library { get; internal set; }
        public int FileCount { get { return Sources.Count; } }
        public bool HasBoardLayout { get; internal set; }
        public readonly List<ImportWarning> Warnings = new List<ImportWarning>();
        public int SkippedSourceRows { get { return Warnings.Count(w => String.IsNullOrEmpty(w.PhraseId)); } }
        internal readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal string TemporaryFolder;
        public string WarningReport()
        {
            StringBuilder text = new StringBuilder("快答 · 导入提醒\r\n\r\n原文件未修改。以下记录有无法恢复的图片/文件引用或位置标记。\r\n可用文字和已有附件继续导入，仅移除没有对应附件的位置标记。\r\n仅含缺失附件、没有可用正文的记录会跳过。原始正文及引用保留在本报告中，请核对后补充。\r\n");
            foreach (ImportWarning warning in Warnings)
            {
                Phrase phrase = Library.Phrases.FirstOrDefault(p => p.Id == warning.PhraseId);
                text.Append("\r\n────────────────────\r\n").Append(warning.Description).Append("\r\n关键词：").Append(phrase == null ? warning.Keyword : phrase.Keyword)
                    .Append("\r\n原始正文（供核对）：\r\n").Append(warning.OriginalText)
                    .Append("\r\n\r\n待导入正文：\r\n").Append(phrase == null ? "（没有可用正文，此行已跳过）" : phrase.Text)
                    .Append("\r\n\r\n现有附件：\r\n").Append(phrase == null ? "无" : String.Join("\r\n", phrase.Attachments.Select((a, i) => (i + 1) + ". " + a.Name))).Append("\r\n");
                if (!String.IsNullOrEmpty(warning.OriginalReferences)) text.Append("\r\n原始图片/文件引用（未导入）：\r\n").Append(warning.OriginalReferences).Append("\r\n");
            }
            return text.ToString();
        }
        public void Dispose()
        {
            if (TemporaryFolder != null && Directory.Exists(TemporaryFolder))
                try { Directory.Delete(TemporaryFolder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            TemporaryFolder = null;
        }
    }

    public sealed class ImportResult
    {
        public int Added { get; internal set; }
        public int Skipped { get; internal set; }
        public int AddedCategories { get; internal set; }
        public int AddedBoards { get; internal set; }
        public string BackupPath { get; internal set; }
        public string ReportPath { get; internal set; }
    }

    public static class LibraryTransfer
    {
        private const string Marker = "QuickReplyLite.PhrasePack";
        private const string Manifest = "快答话术包.json";
        private const long MaxFile = 512L * 1024 * 1024;
        private const long MaxTotal = 1024L * 1024 * 1024;
        private const int MaxJson = 20 * 1024 * 1024;

        internal static string Digest(string file)
        { using (SHA256 hash = SHA256.Create()) using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }

        private static Phrase Clone(Phrase phrase, string category)
        {
            return new Phrase { Id = phrase.Id, CategoryId = category, Keyword = phrase.Keyword, ColorHex = phrase.ColorHex, Text = phrase.Text, InlineAttachments = phrase.InlineAttachments,
                Attachments = phrase.Attachments.Select(a => new Attachment { Name = a.Name, RelativePath = a.RelativePath }).ToList() };
        }

        internal static string EntryName(string name)
        {
            string value = (name ?? "").Replace('\\', '/');
            if (value.Length == 0 || value.StartsWith("/") || value.Contains(":") || value.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                throw new InvalidDataException("话术包包含无效路径。");
            return value;
        }

        internal static string SafeName(string name)
        {
            string value = (name ?? "附件").Replace('\\', '/'); value = value.Substring(value.LastIndexOf('/') + 1);
            foreach (char bad in "<>:\"/\\|?*") value = value.Replace(bad, '_');
            value = new string(value.Select(c => c < 32 ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (value.Length == 0) value = "附件";
            if (value.Length > 160) value = value.Substring(0, 140) + Path.GetExtension(value).Substring(0, Math.Min(20, Path.GetExtension(value).Length));
            string stem = Path.GetFileNameWithoutExtension(value).ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) value = "_" + value;
            return value;
        }

        internal static void CopyLimited(Stream source, Stream destination, long limit)
        {
            byte[] buffer = new byte[65536]; long written = 0; int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            { written += read; if (written > limit) throw new InvalidDataException("话术包中的文件超过允许大小。"); destination.Write(buffer, 0, read); }
        }

        private static Document ExportDocument(LocalStore store, string categoryId, string boardId = null)
        {
            Document doc = new Document();
            doc.Categories = store.Data.Categories.Where(c => (String.IsNullOrEmpty(categoryId) || c.Id == categoryId) && (String.IsNullOrEmpty(boardId) || c.BoardId == boardId)).Select(c => new Category { Id = c.Id, Name = c.Name, BoardId = c.BoardId }).ToList();
            HashSet<string> includedBoards = new HashSet<string>(doc.Categories.Select(c => c.BoardId));
            if (!String.IsNullOrEmpty(boardId)) includedBoards.Add(boardId);
            doc.Boards = store.Data.Boards.Where(b => (String.IsNullOrEmpty(categoryId) && String.IsNullOrEmpty(boardId)) || b.Id == Board.PublicId || b.Id == Board.PersonalId || includedBoards.Contains(b.Id)).Select(b => new Board { Id = b.Id, Name = b.Name }).ToList();
            HashSet<string> categories = new HashSet<string>(doc.Categories.Select(c => c.Id));
            doc.Phrases = store.Data.Phrases.Where(p => categories.Contains(p.CategoryId)).Select(p => Clone(p, p.CategoryId)).ToList();
            LocalStore.Validate(doc); return doc;
        }

        private static void ProtectData(LocalStore store, string output)
        {
            string target = Path.GetFullPath(output), folder = Path.GetFullPath(store.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) throw new IOException("请将导出文件保存到数据文件夹以外的位置。");
        }

        private static void CommitFile(string temp, string output)
        { if (File.Exists(output)) File.Replace(temp, output, null); else File.Move(temp, output); }

        public static void ExportPackage(LocalStore store, string output, string categoryId, string boardId = null)
        {
            ProtectData(store, output);
            Document doc = ExportDocument(store, categoryId, boardId);
            LibraryPackage package = new LibraryPackage { Format = Marker, Version = 3, Library = doc, Files = new List<PackageFile>() };
            Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (Attachment attachment in doc.Phrases.SelectMany(p => p.Attachments))
            {
                string entry = EntryName(attachment.RelativePath);
                if (sources.ContainsKey(entry)) continue;
                string file = store.Resolve(attachment); long size = new FileInfo(file).Length;
                total += size; if (size > MaxFile || total > MaxTotal) throw new IOException("单个附件最多512 MB，完整话术包附件总量最多1 GB。");
                sources.Add(entry, file); package.Files.Add(new PackageFile { Path = entry, Size = size, Sha256 = Digest(file) });
            }
            string temp = output + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    using (Stream content = archive.CreateEntry(Manifest, CompressionLevel.Optimal).Open())
                    using (MemoryStream json = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(LibraryPackage)).WriteObject(json, package);
                        if (json.Length > MaxJson) throw new InvalidDataException("话术包清单超过20 MB。");
                        json.Position = 0; json.CopyTo(content);
                    }
                    foreach (PackageFile file in package.Files)
                        using (FileStream input = File.OpenRead(sources[file.Path]))
                        using (Stream entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal).Open()) CopyLimited(input, entry, file.Size);
                }
                CommitFile(temp, output);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static ImportedLibrary Load(string path)
        {
            if (new[] { ".xls", ".xlsx" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return ExcelLibraryReader.Load(path);
            if (String.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)) return LoadCsv(path);
            if (String.Equals(Path.GetExtension(path), ".bangkefu", StringComparison.OrdinalIgnoreCase)) return BangkefuReader.Load(path);
            using (FileStream probe = File.OpenRead(path))
            {
                if (probe.Length > MaxTotal + MaxJson) throw new InvalidDataException("导入文件超过允许大小。");
                if (probe.ReadByte() != 'P' || probe.ReadByte() != 'K')
                {
                    throw new InvalidDataException("无法识别文件。请选择快答话术包（.quickreply）、Excel（.xls/.xlsx）或 UTF-8 CSV 表格。");
                }
            }
            ImportedLibrary result = new ImportedLibrary();
            try
            {
                using (FileStream input = File.OpenRead(path))
                using (ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count > 90001) throw new InvalidDataException("话术包中文件数量过多。");
                    Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                    long total = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = EntryName(entry.FullName);
                        if (entries.ContainsKey(name)) throw new InvalidDataException("话术包中存在重名文件。");
                        total += entry.Length;
                        if (entry.Length > MaxFile || total > MaxTotal + MaxJson) throw new InvalidDataException("话术包解压后超过允许大小。");
                        entries.Add(name, entry);
                    }
                    ZipArchiveEntry manifest;
                    if (!entries.TryGetValue(Manifest, out manifest) || manifest.Length > MaxJson) throw new InvalidDataException("该压缩包不是快答话术包。");
                    LibraryPackage package;
                    using (Stream source = manifest.Open()) using (MemoryStream json = new MemoryStream())
                    { CopyLimited(source, json, MaxJson); json.Position = 0; package = (LibraryPackage)new DataContractJsonSerializer(typeof(LibraryPackage)).ReadObject(json); }
                    if (package == null || package.Format != Marker || (package.Version != 1 && package.Version != 2 && package.Version != 3) || package.Files == null) throw new InvalidDataException("不支持这个版本的话术包。");
                    if (package.Version == 1 && package.Library != null && package.Library.Phrases != null && package.Library.Phrases.Any(p => p.InlineAttachments)) throw new InvalidDataException("图文混排话术包需要使用v2格式。");
                    LocalStore.Validate(package.Library); result.Library = package.Library; result.HasBoardLayout = package.Version == 3;
                    if (package.Version == 3 && package.Library.Migrated) throw new InvalidDataException("v3话术包缺少板块信息。");
                    if (package.Version < 3 && package.Library.Categories.Any(c => c.BoardId != Board.PersonalId)) throw new InvalidDataException("包含板块信息的话术包需要v3格式。");
                    HashSet<string> required = new HashSet<string>(package.Library.Phrases.SelectMany(p => p.Attachments).Select(a => EntryName(a.RelativePath)), StringComparer.OrdinalIgnoreCase);
                    if (required.Count != package.Files.Count || entries.Count != package.Files.Count + 1) throw new InvalidDataException("话术包的附件清单不完整。");
                    result.TemporaryFolder = Path.Combine(Path.GetTempPath(), "quick-reply-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(result.TemporaryFolder);
                    foreach (PackageFile file in package.Files)
                    {
                        string name = EntryName(file.Path); ZipArchiveEntry entry;
                        if (!required.Remove(name) || !entries.TryGetValue(name, out entry) || file.Size < 0 || file.Size > MaxFile || file.Size != entry.Length || String.IsNullOrEmpty(file.Sha256)) throw new InvalidDataException("附件清单无效。");
                        string staged = Path.Combine(result.TemporaryFolder, Guid.NewGuid().ToString("N"));
                        using (Stream source = entry.Open()) using (FileStream target = File.Create(staged)) CopyLimited(source, target, file.Size);
                        if (new FileInfo(staged).Length != file.Size || !String.Equals(Digest(staged), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("附件损坏或校验失败，请重新导出话术包。");
                        result.Sources.Add(name, staged); result.Hashes.Add(name, file.Sha256.ToLowerInvariant());
                    }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static string Fingerprint(Phrase phrase, string categoryName, string boardName, Func<Attachment, string> hash)
        {
            StringBuilder key = new StringBuilder();
            foreach (string field in new[] { boardName.ToUpperInvariant(), categoryName.ToUpperInvariant(), phrase.Keyword, phrase.ColorHex ?? "", phrase.Text, phrase.InlineAttachments ? "inline" : "plain" }.Concat(phrase.Attachments.Select(a => a.Name + "\n" + hash(a)))) key.Append(field.Length).Append(':').Append(field);
            using (SHA256 digest = SHA256.Create()) return Convert.ToBase64String(digest.ComputeHash(Encoding.UTF8.GetBytes(key.ToString())));
        }

        public static ImportResult Import(LocalStore store, ImportedLibrary incoming, bool replace, bool skipDuplicates, string targetBoardId = null)
        {
            LocalStore.Validate(incoming.Library);
            bool selectedTarget = !String.IsNullOrEmpty(targetBoardId);
            bool preserveLayout = incoming.HasBoardLayout && !selectedTarget;
            bool replaceTarget = replace && selectedTarget;
            if (!selectedTarget) targetBoardId = Board.PersonalId;
            List<Category> oldCategories = store.Data.Categories.ToList(); List<Phrase> oldPhrases = store.Data.Phrases.ToList();
            List<Board> oldBoards = store.Data.Boards.ToList();
            List<Board> boards = replace && preserveLayout ? Board.Defaults() : oldBoards.ToList();
            HashSet<string> replacedCategories = new HashSet<string>(oldCategories.Where(c => c.BoardId == targetBoardId).Select(c => c.Id));
            List<Category> categories = replaceTarget ? oldCategories.Where(c => c.BoardId != targetBoardId).ToList() : replace ? new List<Category>() : oldCategories.ToList();
            List<Phrase> phrases = replaceTarget ? oldPhrases.Where(p => !replacedCategories.Contains(p.CategoryId)).ToList() : replace ? new List<Phrase>() : oldPhrases.ToList();
            Dictionary<string, string> mapping = new Dictionary<string, string>();
            ImportResult result = new ImportResult();
            Dictionary<string, string> boardMapping = new Dictionary<string, string>();
            if (!preserveLayout && !boards.Any(b => b.Id == targetBoardId)) throw new InvalidDataException("请选择存在的目标大板块。");
            foreach (Board board in incoming.Library.Boards)
            {
                if (!preserveLayout) { boardMapping.Add(board.Id, targetBoardId); continue; }
                Board local = boards.FirstOrDefault(b => String.Equals(b.Name, board.Name, StringComparison.OrdinalIgnoreCase));
                if (local == null) { local = new Board { Id = Guid.NewGuid().ToString("N"), Name = board.Name }; boards.Add(local); result.AddedBoards++; }
                boardMapping.Add(board.Id, local.Id);
            }
            foreach (Category category in incoming.Library.Categories)
            {
                string boardId = boardMapping[category.BoardId];
                Category local = categories.FirstOrDefault(c => c.BoardId == boardId && String.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase));
                if (local == null) { local = new Category { Id = Guid.NewGuid().ToString("N"), Name = category.Name, BoardId = boardId }; categories.Add(local); result.AddedCategories++; }
                mapping.Add(category.Id, local.Id);
            }
            Dictionary<string, string> localHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> fingerprints = new HashSet<string>();
            if (!replace && skipDuplicates)
                foreach (Phrase phrase in oldPhrases)
                    fingerprints.Add(Fingerprint(phrase, oldCategories.First(c => c.Id == phrase.CategoryId).Name, oldBoards.First(b => b.Id == oldCategories.First(c => c.Id == phrase.CategoryId).BoardId).Name, delegate(Attachment attachment)
                    {
                        string path = store.Resolve(attachment), digest;
                        if (!localHashes.TryGetValue(path, out digest)) { digest = File.Exists(path) ? Digest(path) : "missing:" + path; localHashes.Add(path, digest); }
                        return digest;
                    }));
            Dictionary<string, string> newPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> copies = new Dictionary<string, string>();
            foreach (Phrase phrase in incoming.Library.Phrases)
            {
                Category incomingCategory = incoming.Library.Categories.First(c => c.Id == phrase.CategoryId);
                string fingerprint = Fingerprint(phrase, incomingCategory.Name, boards.First(b => b.Id == boardMapping[incomingCategory.BoardId]).Name, a => incoming.Hashes[EntryName(a.RelativePath)]);
                if (!replace && skipDuplicates && !fingerprints.Add(fingerprint)) { result.Skipped++; continue; }
                Phrase added = Clone(phrase, mapping[phrase.CategoryId]); added.Id = Guid.NewGuid().ToString("N");
                foreach (Attachment attachment in added.Attachments)
                {
                    string original = EntryName(attachment.RelativePath), relative;
                    if (!newPaths.TryGetValue(original, out relative))
                    {
                        relative = "素材/" + Guid.NewGuid().ToString("N") + "/" + SafeName(attachment.Name);
                        newPaths.Add(original, relative); copies.Add(relative, incoming.Sources[original]);
                    }
                    attachment.RelativePath = relative;
                }
                phrases.Add(added); result.Added++;
            }
            Document candidate = new Document { Categories = categories, Phrases = phrases, Preferences = store.Data.Preferences, Boards = boards };
            LocalStore.Validate(candidate);
            bool changed = replace || result.Added != 0 || result.AddedCategories != 0 || result.AddedBoards != 0;
            if (!changed && incoming.Warnings.Count == 0) return result;
            List<string> created = new List<string>();
            try
            {
                if (incoming.Warnings.Count > 0)
                {
                    result.ReportPath = Path.Combine(store.Folder, "导入提醒-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt");
                    File.WriteAllText(result.ReportPath, incoming.WarningReport(), new UTF8Encoding(true));
                }
                if (!changed) return result;
                result.BackupPath = Path.Combine(store.Folder, "话术.导入前-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
                File.Copy(store.FilePath, result.BackupPath, false);
                foreach (KeyValuePair<string, string> copy in copies)
                {
                    string destination = Path.Combine(store.Folder, copy.Key.Replace('/', Path.DirectorySeparatorChar));
                    string directory = Path.GetDirectoryName(destination); Directory.CreateDirectory(directory); created.Add(directory); File.Copy(copy.Value, destination, false);
                }
                store.Data.Boards.Clear(); store.Data.Boards.AddRange(boards); store.Data.Categories.Clear(); store.Data.Categories.AddRange(categories); store.Data.Phrases.Clear(); store.Data.Phrases.AddRange(phrases); store.Save();
            }
            catch
            {
                store.Data.Boards.Clear(); store.Data.Boards.AddRange(oldBoards); store.Data.Categories.Clear(); store.Data.Categories.AddRange(oldCategories); store.Data.Phrases.Clear(); store.Data.Phrases.AddRange(oldPhrases);
                foreach (string directory in created) try { Directory.Delete(directory, true); } catch { }
                if (result.ReportPath != null) try { File.Delete(result.ReportPath); } catch { }
                throw;
            }
            return result;
        }

        private static string CsvCell(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }

        public static int ExportCsv(LocalStore store, string output, string categoryId)
        {
            ProtectData(store, output); Document doc = ExportDocument(store, categoryId);
            string temp = output + ".tmp-" + Guid.NewGuid().ToString("N"); int count = 0;
            try
            {
                using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("板块,分类,关键词,关键词底色,话术内容");
                    foreach (Phrase phrase in doc.Phrases.Where(p => !String.IsNullOrWhiteSpace(PhraseContent.PlainText(p))))
                    { Category category = doc.Categories.First(c => c.Id == phrase.CategoryId); writer.WriteLine(String.Join(",", new[] { doc.Boards.First(b => b.Id == category.BoardId).Name, category.Name, phrase.Keyword, phrase.ColorHex, PhraseContent.PlainText(phrase) }.Select(CsvCell))); count++; }
                }
                CommitFile(temp, output); return count;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static List<List<string>> ParseCsv(string text)
        {
            List<List<string>> rows = new List<List<string>>(); List<string> row = new List<string>(); StringBuilder cell = new StringBuilder(); bool quoted = false, endedQuote = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; endedQuote = true; } }
                    else cell.Append(c);
                }
                else if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); endedQuote = false;
                    if (row.Count > 40) throw new InvalidDataException("CSV列数过多。");
                    if (c != ',') { rows.Add(row); row = new List<string>(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
                    if (rows.Count > 10001) throw new InvalidDataException("CSV最多导入10000条话术。");
                }
                else if (c == '"' && cell.Length == 0 && !endedQuote) quoted = true;
                else { if (endedQuote || c == '"') throw new InvalidDataException("CSV引号格式不正确。"); cell.Append(c); }
            }
            if (quoted) throw new InvalidDataException("CSV中存在未闭合的引号。");
            if (cell.Length > 0 || row.Count > 0 || endedQuote) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        private static ImportedLibrary LoadCsv(string path)
        {
            if (new FileInfo(path).Length > MaxJson) throw new InvalidDataException("CSV文件超过20 MB。");
            string text;
            try { using (StreamReader reader = new StreamReader(path, new UTF8Encoding(false, true), true)) text = reader.ReadToEnd(); }
            catch (DecoderFallbackException) { throw new InvalidDataException("请将表格另存为 UTF-8 CSV 后再导入。"); }
            List<List<string>> rows = ParseCsv(text); if (rows.Count == 0) throw new InvalidDataException("CSV没有内容。");
            ImportedLibrary result = new ImportedLibrary { Library = new Document() };
            AppendTable(result, rows, "CSV");
            if (result.Library.Phrases.Count == 0) throw new InvalidDataException("CSV中没有可导入的非空话术正文。");
            LocalStore.Validate(result.Library); return result;
        }

        private static string Header(string value)
        { return (value ?? "").Trim().TrimStart('\uFEFF').Replace("（必填）", "").Replace("(必填)", "").Trim(); }
        internal static bool IsTableHeader(List<string> row)
        { return row.Select(Header).Any(h => new[] { "话术", "话术内容", "快捷回复内容", "回复内容", "内容", "正文" }.Contains(h)); }

        internal static void AppendTable(ImportedLibrary result, List<List<string>> rows, string source, List<int> sourceRows = null)
        {
            List<string> headers = rows[0].Select(Header).ToList();
            Func<string[], int> column = names => headers.FindIndex(h => names.Contains(h));
            int body = column(new[] { "话术", "话术内容", "快捷回复内容", "回复内容", "内容", "正文" });
            int keyword = column(new[] { "关键词", "关键字", "回复问题", "问题" }); int color = column(new[] { "关键词底色", "背景色", "底色" });
            int category = column(new[] { "分类", "一级分类", "分类名称" }); int second = column(new[] { "二级分类" });
            int boardColumn = column(new[] { "板块", "板块名称" });
            List<int> assetColumns = headers.Select((h, i) => new { Header = h, Index = i }).Where(c => new[] { "图片", "附件", "文件" }.Contains(c.Header)).Select(c => c.Index).ToList();
            if (body < 0) throw new InvalidDataException(source + "需包含“话术”或“话术内容”列。");
            result.HasBoardLayout |= boardColumn >= 0;
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                Func<int, string> cell = index => index < 0 || index >= row.Count ? "" : row[index];
                string original = cell(body), bodyText = PhraseContent.Marker.Replace(original, "");
                string references = String.Join("\r\n", assetColumns.Where(i => !String.IsNullOrWhiteSpace(cell(i))).Select(i => headers[i] + "：" + cell(i)));
                ImportWarning warning = null;
                if (references.Length > 0 || bodyText != original)
                {
                    warning = new ImportWarning { OriginalText = original, OriginalReferences = references, Keyword = cell(keyword),
                        Description = source + "第 " + (sourceRows == null ? rowIndex + 1 : sourceRows[rowIndex]) + " 行：表格未提供可恢复的图片/文件；已忽略附件引用和位置标记，保留其余文字。" };
                    result.Warnings.Add(warning);
                }
                if (String.IsNullOrWhiteSpace(bodyText))
                {
                    if (warning != null) warning.Description += "此行没有可用正文，已跳过。";
                    continue;
                }
                string colorValue = cell(color).Trim().TrimStart('#');
                if (colorValue.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(colorValue, @"^[0-9a-fA-F]{6}$")) throw new InvalidDataException(source + "存在无效的背景色，需要6位十六进制颜色值。");
                if (result.Library.Phrases.Count >= 10000) throw new InvalidDataException("最多导入10000条话术。");
                string name = cell(category).Trim(); if (name.Length == 0) name = "导入的话术";
                if (cell(second).Trim().Length > 0) name += " / " + cell(second).Trim();
                string boardName = cell(boardColumn).Trim(); if (boardName.Length == 0) boardName = "个人";
                Board board = result.Library.Boards.FirstOrDefault(b => String.Equals(b.Name, boardName, StringComparison.OrdinalIgnoreCase));
                if (board == null) { board = new Board { Id = Guid.NewGuid().ToString("N"), Name = boardName }; result.Library.Boards.Add(board); }
                Category group = result.Library.Categories.FirstOrDefault(c => c.BoardId == board.Id && String.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (group == null) { group = new Category { Id = Guid.NewGuid().ToString("N"), Name = name, BoardId = board.Id }; result.Library.Categories.Add(group); }
                Phrase phrase = new Phrase { Id = Guid.NewGuid().ToString("N"), CategoryId = group.Id, Text = bodyText, Keyword = cell(keyword), ColorHex = colorValue.Length == 0 ? "" : "#" + colorValue.ToUpperInvariant() };
                result.Library.Phrases.Add(phrase);
                if (warning != null) warning.PhraseId = phrase.Id;
            }
        }
    }
}
