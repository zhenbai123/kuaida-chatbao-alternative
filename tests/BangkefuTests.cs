using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickReplyLite;

internal static class BangkefuTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string reference = "uploadimages/test/photo.png", name;
        using (MD5 md5 = MD5.Create()) name = "images/" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(reference))).Replace("-", "").ToLowerInvariant() + ".png";
        string csv = "常用,,混排,前文#图1#后文^J下一行," + reference + ",9C27B0\r\n售后,退款,说明,请稍等,,FFFFFF\r\n常用,,纯图片,#图1#," + reference + ",3A84FF\r\n";
        byte[] image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQ0AAAAASUVORK5CYII=");
        string plain = Path.Combine(root, "plain.bangkefu"), encrypted = Path.Combine(root, "encrypted.bangkefu");
        using (FileStream file = File.Create(plain)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using (Stream stream = zip.CreateEntry("export.csv", CompressionLevel.NoCompression).Open()) { byte[] bytes = Encoding.UTF8.GetBytes(csv); stream.Write(bytes, 0, bytes.Length); }
            using (Stream stream = zip.CreateEntry(name).Open()) stream.Write(image, 0, image.Length);
        }
        Encrypt(plain, encrypted);
        using (ImportedLibrary incoming = LibraryTransfer.Load(encrypted))
        {
            check(incoming.Library.Phrases.Count == 3 && incoming.Library.Categories.Count == 2 && incoming.FileCount == 1, "bangkefu AES-CBC reader restores headerless CSV, nested categories and deduplicated image files");
            Phrase phrase = incoming.Library.Phrases[0];
            check(phrase.Text == "前文#图1#后文\r\n下一行" && phrase.ColorHex == "#9C27B0" && phrase.Keyword == "混排" && incoming.Library.Categories[1].Name == "售后 / 退款", "legacy newlines, keyword, exact color and category names are restored");
            var parts = PhraseContent.Build(phrase);
            check(parts.Count == 3 && parts[0].Text == "前文" && parts[1].AttachmentIndex == 0 && parts[2].Text == "后文\r\n下一行", "image markers become ordered send parts between text chunks");
            check(PhraseContent.PlainText(phrase) == "前文后文\r\n下一行" && PhraseContent.Build(incoming.Library.Phrases[2]).Count == 1, "copyable text omits image markers and image-only phrases remain sendable");
            check(File.ReadAllBytes(incoming.Sources[phrase.Attachments[0].RelativePath]).SequenceEqual(image), "legacy asset reference maps to the MD5-named image with exact bytes");
            LocalStore target = new LocalStore(Path.Combine(root, "target")); LibraryTransfer.Import(target, incoming, true, true);
            string pack = Path.Combine(root, "converted.quickreply"); LibraryTransfer.ExportPackage(target, pack, "");
            using (ImportedLibrary roundtrip = LibraryTransfer.Load(pack))
                check(roundtrip.Library.Phrases[0].InlineAttachments && PhraseContent.Build(roundtrip.Library.Phrases[0]).Count == 3 && roundtrip.FileCount == 1, "quickreply export and re-import preserve legacy image positions");
            check(new LocalStore(target.Folder).Data.Phrases[0].InlineAttachments, "inline image layout survives JSON save and restart");
            LibraryTransfer.Import(target, incoming, false, true);
            check(target.Data.Phrases.Count == 3, "re-importing the same bangkefu library skips exact duplicates");
        }
        using (ImportedLibrary incoming = LibraryTransfer.Load(plain)) check(incoming.Library.Phrases.Count == 3, "unencrypted ZIP variant of bangkefu is also readable");
        check(PhraseContent.RemoveAttachmentMarker("前#图1#中#图2#后#文件3#", 2) == "前#图1#中后#文件2#", "removing an inline attachment deletes its marker and renumbers later markers");
        string truncated = Path.Combine(root, "truncated.bangkefu"); File.WriteAllBytes(truncated, File.ReadAllBytes(encrypted).Take(19).ToArray());
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(truncated)) { } }, "truncated encrypted libraries are rejected", check);
        byte[] modified = File.ReadAllBytes(plain); byte[] needle = Encoding.UTF8.GetBytes("前文");
        int position = Find(modified, needle); modified[position] ^= 1; File.WriteAllBytes(plain, modified); Encrypt(plain, truncated);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(truncated)) { } }, "legacy CSV CRC detects tampering even when encryption padding is valid", check);
        Phrase invalid = new Phrase { Id = "bad", CategoryId = "c", Keyword = "", Text = "#图2#", InlineAttachments = true }; invalid.Attachments.Add(new Attachment { Name = "a", RelativePath = "素材/a" });
        Reject(delegate { PhraseContent.Build(invalid); }, "out-of-range inline image references are rejected before sending", check);
        MissingMarkers(Path.Combine(root, "missing-markers"), reference, name, image, check);
        CompatibilityMatrix(Path.Combine(root, "compatibility-matrix"), image, check);
        HeaderAndColors(Path.Combine(root, "header-colors"), reference, name, image, check);
    }

    private static void HeaderAndColors(string root, string reference, string name, byte[] image, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string header = "一级分类（必填）,二级分类,关键词,话术（必填）,图片,背景色\r\n";
        string[] colors = { "00BCD4", "060606", "FF9800", "F44336", "3F51B5", "9C27B0", "4CAF50", "3A84FF", "FFFFFF", "#12aBcD", "" };
        string body = "";
        for (int i = 0; i < colors.Length; i++) body += String.Join(",", new[] { "演示分类", "", "颜色" + i, "前文" + (i == 0 ? "#图1#" : "") + "^J后文", i == 0 ? reference : "", colors[i] }.Select(Cell)) + "\r\n";
        for (int variant = 0; variant < 4; variant++)
        {
            bool withHeader = variant >= 2, encrypted = variant % 2 == 1;
            string path = Path.Combine(root, "variant-" + variant + ".bangkefu"), cipher = Path.Combine(root, "cipher-" + variant + ".bangkefu");
            WriteArchive(path, (withHeader ? "\ufeff\r\n" + header : "") + body, name, image);
            if (encrypted) { Encrypt(path, cipher); path = cipher; }
            using (ImportedLibrary incoming = LibraryTransfer.Load(path))
            {
                check(incoming.Library.Phrases.Count == colors.Length && incoming.Library.Categories.Count == 1 && incoming.Library.Categories[0].Name == "演示分类"
                    && incoming.Library.Phrases.Select(p => p.ColorHex).SequenceEqual(colors.Select(c => c.Length == 0 ? "" : "#" + c.TrimStart('#').ToUpperInvariant())),
                    "bangkefu variant " + variant + ": complete optional header is excluded and every original or custom RGB color is preserved independently of the editor palette");
                check(incoming.Warnings.Count == 0 && incoming.FileCount == 1 && incoming.Library.Phrases[0].Keyword == "颜色0" && incoming.Library.Phrases[0].Text == "前文#图1#\r\n后文"
                    && File.ReadAllBytes(incoming.Sources[incoming.Library.Phrases[0].Attachments[0].RelativePath]).SequenceEqual(image),
                    "bangkefu variant " + variant + ": header detection leaves the first real phrase, inline image bytes and newlines intact");
            }
        }
        string plain = Path.Combine(root, "edge-cases.bangkefu");
        WriteArchive(plain, " 一级分类(必填) , 二级分类 , 关键词 , 话术(必填) , 图片 , 背景色 \r\n常用,,问候,您好,,#12abcd\r\n", name, image);
        using (ImportedLibrary incoming = LibraryTransfer.Load(plain)) check(incoming.Library.Phrases.Count == 1 && incoming.Library.Phrases[0].ColorHex == "#12ABCD", "known export headers accept surrounding whitespace and either Chinese or ASCII required-field parentheses");
        WriteArchive(plain, "一级分类（必填）,二级分类,关键词,话术（必填）,,060606\r\n", name, image);
        using (ImportedLibrary incoming = LibraryTransfer.Load(plain)) check(incoming.Library.Phrases.Count == 1 && incoming.Library.Phrases[0].Text == "话术（必填）", "a real first phrase resembling column labels is retained when the complete schema does not match");
        WriteArchive(plain, header + "常用,,问候,您好,,not-a-color\r\n", name, image);
        bool located = false; try { using (ImportedLibrary ignored = LibraryTransfer.Load(plain)) { } } catch (InvalidDataException error) { located = error.Message.Contains("第 2 行") && error.Message.Contains("颜色值"); }
        check(located, "genuinely invalid colors report the source row instead of silently changing or dropping a phrase");
        WriteArchive(plain, header, name, image);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(plain)) { } }, "a header-only knowledge base cannot create a bogus phrase from column titles", check);
        WriteArchive(plain, "常用,,问候,您好,,FFFFFF\r\n" + header, name, image);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(plain)) { } }, "header detection is limited to the leading record and cannot discard unexpected records inside the library", check);
    }

    private static void MissingMarkers(string root, string reference, string name, byte[] image, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        string plain = Path.Combine(root, "missing-plain.bangkefu"), encrypted = Path.Combine(root, "missing.bangkefu");
        string originalText = "前文#图1#中#图2#后\r\n尾#图0##图片999999999999999999999#";
        string csv = "常用,,缺图," + originalText.Replace("\r\n", "^J") + "," + reference + ",9C27B0\r\n常用,,无附件,无图#文件1#说明,,FFFFFF\r\n常用,,未放标记,未放标记," + reference + ",3A84FF\r\n常用,,完整,完整内容,,FFFFFF\r\n";
        WriteArchive(plain, csv, name, image); Encrypt(plain, encrypted); string sourceHash = LibraryTransfer.Digest(encrypted);
        using (ImportedLibrary incoming = LibraryTransfer.Load(encrypted))
        {
            check(incoming.Library.Phrases.Count == 4 && incoming.Warnings.Count == 2 && incoming.FileCount == 1, "dangling legacy markers no longer block healthy phrases or available attachments");
            Phrase first = incoming.Library.Phrases[0];
            check(first.Text == "前文#图1#中后\r\n尾" && first.Keyword == "缺图" && first.ColorHex == "#9C27B0" && first.Attachments.Count == 1 && PhraseContent.Build(first).Count == 3,
                "legacy repair removes only invalid, zero or overflowing markers and preserves valid image positions, text and metadata");
            check(incoming.Warnings[0].OriginalText == originalText && incoming.Warnings[0].Description.Contains("#图2#") && incoming.Warnings[0].Description.Contains("第 1 条") && incoming.Warnings[0].PhraseId == first.Id,
                "repair warnings identify the source row and retain the exact original body");
            check(incoming.Library.Phrases[1].Text == "无图说明" && !incoming.Library.Phrases[1].InlineAttachments && PhraseContent.Build(incoming.Library.Phrases[2]).Last().AttachmentIndex == 0,
                "legacy markers with no attachments are reported, and unmarked existing images remain sendable");
            LocalStore target = new LocalStore(Path.Combine(root, "target"));
            ImportResult imported = LibraryTransfer.Import(target, incoming, false, true);
            string report = File.ReadAllText(imported.ReportPath);
            check(imported.Added == 4 && File.Exists(imported.BackupPath) && report.Contains(originalText) && report.Contains(first.Text) && report.Contains("#文件1#") && File.ReadAllBytes(imported.ReportPath).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }),
                "successful import saves a Windows-readable report with original and repaired text alongside its data backup");
            byte[] saved = File.ReadAllBytes(target.FilePath); int reportCount = Directory.GetFiles(target.Folder, "导入提醒-*.txt").Length;
            ImportResult repeated = LibraryTransfer.Import(target, incoming, false, true);
            check(repeated.Added == 0 && repeated.Skipped == 4 && File.Exists(repeated.ReportPath) && saved.SequenceEqual(File.ReadAllBytes(target.FilePath)),
                "repeat import skips repaired duplicates without rewriting data while retaining a warning report");
            Directory.CreateDirectory(target.FilePath + ".tmp");
            Reject(delegate { LibraryTransfer.Import(target, incoming, true, true); }, "failed save of a repaired import still reports the failure", check);
            check(saved.SequenceEqual(File.ReadAllBytes(target.FilePath)) && target.Data.Phrases.Count == 7 && Directory.GetFiles(target.Folder, "导入提醒-*.txt").Length == reportCount + 1,
                "failed repaired imports roll back data and remove their unfinished warning report");
            Directory.Delete(target.FilePath + ".tmp");
            string converted = Path.Combine(root, "converted.quickreply"); LibraryTransfer.ExportPackage(target, converted, "");
            using (ImportedLibrary again = LibraryTransfer.Load(converted)) check(again.Library.Phrases.Any(p => p.Text == first.Text && p.InlineAttachments) && again.Warnings.Count == 0,
                "repaired content exports to a valid ordinary quickreply package without dangling markers");
        }
        check(sourceHash == LibraryTransfer.Digest(encrypted), "repair never modifies the original encrypted input file");
        WriteArchive(plain, "常用,,空白,#图1#,,FFFFFF\r\n", name, image);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(plain)) { } }, "a phrase consisting only of missing markers is explicitly rejected rather than silently discarded", check);
        WriteArchive(plain, "常用,,缺文件,#图1#,uploadimages/absent.png,FFFFFF\r\n", name, image);
        Reject(delegate { using (ImportedLibrary ignored = LibraryTransfer.Load(plain)) { } }, "missing archive files referenced by the asset list still fail integrity validation", check);
    }

    private static void WriteArchive(string path, string csv, string name, byte[] image)
    {
        using (FileStream file = File.Create(path)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using (Stream stream = zip.CreateEntry("export.csv").Open()) { byte[] bytes = Encoding.UTF8.GetBytes(csv); stream.Write(bytes, 0, bytes.Length); }
            using (Stream stream = zip.CreateEntry(name).Open()) stream.Write(image, 0, image.Length);
        }
    }

    private static string Cell(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }

    private static void CompatibilityMatrix(string root, byte[] image, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        for (int count = 0; count <= 9; count++)
        {
            string[] references = Enumerable.Range(0, count).Select(i => "arbitrary-server/中文,资料/" + i + (i % 2 == 0 ? ".png" : ".txt") + "?download=1").ToArray();
            string body = "中文,\"引号\" 😊^J开头" + String.Join("中间", Enumerable.Range(1, count).Select(i => "#" + (i % 2 == 0 ? "文件" : "附件") + i + "#")) + (count > 0 ? "重复#图1#" : "") + "末尾";
            string broken = body + "#图片" + (count + 1) + "##图0#";
            string csv = "\ufeff" + String.Join(",", new[] { "分类," + count, "子\"类", "关键词" + count, broken, String.Join("|||", references), "#a1b2c3" }.Select(Cell)) + "\r\n"
                + String.Join(",", new[] { "其他", "", "", "另一条^J正文", "", "" }.Select(Cell)) + "\r\n";
            string plain = Path.Combine(root, "variant-" + count + ".bangkefu"), encrypted = Path.Combine(root, "encrypted-" + count + ".bangkefu");
            using (FileStream file = File.Create(plain)) using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using (Stream stream = zip.CreateEntry("export.csv").Open()) { byte[] bytes = Encoding.UTF8.GetBytes(csv); stream.Write(bytes, 0, bytes.Length); }
                foreach (string reference in references)
                {
                    string name; using (MD5 md5 = MD5.Create()) name = "images/" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(reference))).Replace("-", "").ToLowerInvariant() + (reference.Contains(".png?") ? ".png" : ".txt");
                    byte[] bytes = reference.Contains(".png?") ? image : Encoding.UTF8.GetBytes("独立文件内容 " + reference);
                    using (Stream stream = zip.CreateEntry(name).Open()) stream.Write(bytes, 0, bytes.Length);
                }
            }
            Encrypt(plain, encrypted); string input = count % 2 == 0 ? plain : encrypted, hash = LibraryTransfer.Digest(input), temporary;
            using (ImportedLibrary incoming = LibraryTransfer.Load(input))
            {
                temporary = incoming.TemporaryFolder; Phrase phrase = incoming.Library.Phrases[0];
                check(incoming.Library.Phrases.Count == 2 && incoming.Library.Categories.Count == 2 && incoming.Library.Categories[0].Name == "分类," + count + " / 子\"类"
                    && phrase.Text == body.Replace("^J", "\r\n") && phrase.Keyword == "关键词" + count && phrase.ColorHex == "#A1B2C3" && phrase.Attachments.Count == count && incoming.FileCount == count
                    && PhraseContent.Build(phrase).Where(p => p.AttachmentIndex >= 0).Select(p => p.AttachmentIndex).SequenceEqual(Enumerable.Range(0, count).Concat(count > 0 ? new[] { 0 } : new int[0])),
                    "format compatibility with " + count + " mixed attachments: arbitrary names, quoted CSV/BOM, nested category, Unicode, query references, repeated markers and both archive wrappers");
                check(incoming.Warnings.Count == 1 && incoming.Warnings[0].OriginalText == broken.Replace("^J", "\r\n")
                    && phrase.Attachments.Select((a, i) => File.ReadAllBytes(incoming.Sources[a.RelativePath]).SequenceEqual(i % 2 == 0 ? image : Encoding.UTF8.GetBytes("独立文件内容 " + references[i]))).All(v => v),
                    "format compatibility with " + count + " attachments: each available image/file retains exact bytes and dangling-marker source text is recoverable");
            }
            check(!Directory.Exists(temporary) && hash == LibraryTransfer.Digest(input), "format compatibility variant " + count + ": preview cleanup leaves the original file unchanged");
        }
    }

    internal static void RealFile(string path, string output, string dataFolder, Action<bool, string> check)
    {
        string originalHash = LibraryTransfer.Digest(path); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        bool otherComputer = originalHash == "b6ffe753539f39cbf0a9b7ef256191b76f361abd29c2ab215ece48890beb383c";
        if (!otherComputer && originalHash != "519044036cfb7f9a6c50845343f095fe52bff67e8fc484e5dbf1b2ddfc14c1e9") throw new Exception("This real-file regression accepts only the two independently verified fixtures.");
        int phrases = otherComputer ? 343 : 74, categories = otherComputer ? 28 : 3, files = otherComputer ? 247 : 29, references = otherComputer ? 272 : 30;
        using (ImportedLibrary incoming = LibraryTransfer.Load(path))
        {
            check(incoming.Library.Phrases.Count == phrases && incoming.Library.Categories.Count == categories && incoming.FileCount == files && incoming.Library.Phrases.Sum(p => p.Attachments.Count) == references,
                "actual user library: " + phrases + " phrases, " + categories + " categories, " + files + " image files and " + references + " image references decoded");
            check(incoming.Library.Phrases.Sum(p => p.Text.Count(c => c == '\n')) == (otherComputer ? 463 : 94) && incoming.Library.Phrases.Count(p => p.InlineAttachments) == (otherComputer ? 121 : 16),
                "actual user library: independently counted newlines and image-bearing phrases retained");
            check(otherComputer ? incoming.Warnings.Count == 1 && incoming.Warnings[0].PhraseId == incoming.Library.Phrases[113].Id && incoming.Warnings[0].OriginalText.Contains("#图10#") && incoming.Warnings[0].OriginalText.Contains("#图11#") && !incoming.Library.Phrases[113].Text.Contains("#图10#") && !incoming.Library.Phrases[113].Text.Contains("#图11#") : incoming.Warnings.Count == 0,
                "actual user library: only the independently identified two dangling markers require a source-preserving warning");
            LocalStore store = new LocalStore(dataFolder); ImportResult result = LibraryTransfer.Import(store, incoming, true, true);
            bool equal = result.Added == incoming.Library.Phrases.Count;
            for (int i = 0; i < store.Data.Phrases.Count; i++)
            {
                Phrase left = store.Data.Phrases[i], right = incoming.Library.Phrases[i];
                equal &= left.Text == right.Text && left.Keyword == right.Keyword && left.ColorHex == right.ColorHex && left.InlineAttachments == right.InlineAttachments && left.Attachments.Count == right.Attachments.Count
                    && store.Data.Categories.First(c => c.Id == left.CategoryId).Name == incoming.Library.Categories.First(c => c.Id == right.CategoryId).Name;
                for (int j = 0; j < left.Attachments.Count; j++) equal &= LibraryTransfer.Digest(store.Resolve(left.Attachments[j])) == incoming.Hashes[right.Attachments[j].RelativePath];
            }
            check(equal, "actual user library: every imported body, keyword, color, image position and image byte hash matches the decoded source");
            check(otherComputer ? result.ReportPath != null && File.ReadAllText(result.ReportPath).Contains(incoming.Warnings[0].OriginalText) : result.ReportPath == null, "actual user library: missing-marker source text is retained in a report only when needed");
            if (result.ReportPath != null) File.Copy(result.ReportPath, Path.ChangeExtension(output, ".导入提醒.txt"), true);
            LibraryTransfer.ExportPackage(store, output, "");
            using (ImportedLibrary converted = LibraryTransfer.Load(output)) check(converted.Library.Phrases.Count == phrases && converted.FileCount == files && converted.Library.Phrases.Count(p => p.InlineAttachments) == (otherComputer ? 121 : 16), "actual user library: ready-to-import quickreply package verifies successfully");
            byte[] beforeRepeat = File.ReadAllBytes(store.FilePath); ImportResult repeated = LibraryTransfer.Import(store, incoming, false, true);
            check(repeated.Added == 0 && repeated.Skipped == phrases && beforeRepeat.SequenceEqual(File.ReadAllBytes(store.FilePath)), "actual user library: all repaired and healthy phrases deduplicate on repeated import");
            check(LibraryTransfer.Digest(path) == originalHash, "actual user input file remains byte-for-byte unchanged");
            Console.WriteLine("Converted personal package: " + Path.GetFullPath(output));
        }
    }

    private static int Find(byte[] haystack, byte[] needle)
    { for (int i = 0; i <= haystack.Length - needle.Length; i++) if (needle.Select((b, j) => haystack[i + j] == b).All(v => v)) return i; throw new Exception("test marker missing"); }
    private static void Reject(Action action, string name, Action<bool, string> check)
    { bool failed = false; try { action(); } catch { failed = true; } check(failed, name); }
    private static void Encrypt(string input, string output)
    {
        using (Aes aes = Aes.Create())
        {
            aes.Key = BangkefuReader.Key; aes.IV = BangkefuReader.IV; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            using (FileStream source = File.OpenRead(input)) using (FileStream target = File.Create(output))
            using (ICryptoTransform encrypt = aes.CreateEncryptor()) using (CryptoStream crypto = new CryptoStream(target, encrypt, CryptoStreamMode.Write)) source.CopyTo(crypto);
        }
    }
}
