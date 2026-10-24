using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QuickReplyLite
{
    // Compatibility reader for the encrypted export.csv + images/ archive variant.
    // The constants belong to this file format; no original client or network is needed.
    public static class BangkefuReader
    {
        private const long MaxTotal = 1024L * 1024 * 1024;
        private const int MaxCsv = 20 * 1024 * 1024;
        internal static readonly byte[] Key = Encoding.ASCII.GetBytes("7ed9980b1013dd549b6fd9964c081948");
        internal static readonly byte[] IV = Encoding.ASCII.GetBytes("779c3f1d9c544146");

        private static Dictionary<string, uint> ReadCrcDirectory(string path)
        {
            Dictionary<string, uint> result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            using (FileStream stream = File.OpenRead(path)) using (BinaryReader reader = new BinaryReader(stream))
            {
                int size = (int)Math.Min(stream.Length, 65557); stream.Position = stream.Length - size; byte[] tail = reader.ReadBytes(size); int end = -1;
                for (int i = size - 22; i >= 0; i--)
                    if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == size) { end = i; break; }
                if (end < 0 || BitConverter.ToUInt16(tail, end + 4) != 0 || BitConverter.ToUInt16(tail, end + 6) != 0) throw new InvalidDataException("原知识库ZIP目录不完整。");
                int count = BitConverter.ToUInt16(tail, end + 10); uint offset = BitConverter.ToUInt32(tail, end + 16), length = BitConverter.ToUInt32(tail, end + 12);
                if (count == 65535 || offset == UInt32.MaxValue || (long)offset + length > stream.Length - size + end) throw new InvalidDataException("原知识库ZIP版本尚未支持，或目录已损坏。");
                stream.Position = offset;
                for (int i = 0; i < count; i++)
                {
                    byte[] header = reader.ReadBytes(46);
                    if (header.Length != 46 || BitConverter.ToUInt32(header, 0) != 0x02014b50) throw new InvalidDataException("原知识库ZIP目录损坏。");
                    int nameLength = BitConverter.ToUInt16(header, 28); byte[] nameBytes = reader.ReadBytes(nameLength);
                    string name = new UTF8Encoding(false, true).GetString(nameBytes);
                    if (result.ContainsKey(name)) throw new InvalidDataException("原知识库ZIP目录包含重名项。");
                    result.Add(name, BitConverter.ToUInt32(header, 16));
                    stream.Position += BitConverter.ToUInt16(header, 30) + BitConverter.ToUInt16(header, 32);
                    if (stream.Position > (long)offset + length) throw new InvalidDataException("原知识库ZIP目录越界。");
                }
            }
            return result;
        }

        private static uint Crc32(Stream stream)
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++) { uint value = i; for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1; table[i] = value; }
            uint crc = 0xffffffff; byte[] buffer = new byte[65536]; int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i < count; i++) crc = table[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
            return crc ^ 0xffffffff;
        }

        private static void VerifyCrc(Stream content, ZipArchiveEntry entry, Dictionary<string, uint> crcs)
        {
            uint expected; if (!crcs.TryGetValue(entry.FullName, out expected) || Crc32(content) != expected) throw new InvalidDataException("原知识库附件或话术表校验失败，文件可能已损坏。");
        }

        private static string AssetName(string reference)
        {
            string path = reference.Replace('\\', '/'); int query = path.IndexOfAny(new[] { '?', '#' }); if (query >= 0) path = path.Substring(0, query);
            string extension = Path.GetExtension(path);
            if (!Regex.IsMatch(extension, @"^\.[A-Za-z0-9]{1,12}$")) throw new InvalidDataException("原知识库包含无法识别的附件扩展名。");
            using (MD5 md5 = MD5.Create()) return "images/" + BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(reference))).Replace("-", "").ToLowerInvariant() + extension;
        }

        public static ImportedLibrary Load(string path)
        {
            ImportedLibrary result = new ImportedLibrary { Library = new Document(), TemporaryFolder = Path.Combine(Path.GetTempPath(), "quick-reply-bangkefu-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(result.TemporaryFolder);
            try
            {
                string decoded = Path.Combine(result.TemporaryFolder, "decoded.zip");
                using (FileStream source = File.OpenRead(path))
                {
                    if (source.Length == 0 || source.Length > MaxTotal + MaxCsv) throw new InvalidDataException(".bangkefu 文件大小无效。");
                    bool plain = source.ReadByte() == 'P' && source.ReadByte() == 'K'; source.Position = 0;
                    using (FileStream target = File.Create(decoded))
                    {
                        if (plain) LibraryTransfer.CopyLimited(source, target, MaxTotal + MaxCsv);
                        else
                        {
                            if (source.Length % 16 != 0) throw new InvalidDataException(".bangkefu 文件已截断，或使用了尚未支持的格式版本。");
                            using (Aes aes = Aes.Create())
                            {
                                aes.Key = Key; aes.IV = IV; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                                using (ICryptoTransform decrypt = aes.CreateDecryptor())
                                using (CryptoStream crypto = new CryptoStream(source, decrypt, CryptoStreamMode.Read)) LibraryTransfer.CopyLimited(crypto, target, MaxTotal + MaxCsv);
                            }
                        }
                    }
                }
                Dictionary<string, uint> crcs = ReadCrcDirectory(decoded);
                using (FileStream input = File.OpenRead(decoded))
                using (ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count > 90001) throw new InvalidDataException("原知识库中的文件数量过多。");
                    Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase); long total = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = LibraryTransfer.EntryName(entry.FullName.TrimEnd('/'));
                        if (entry.FullName.EndsWith("/") && entry.Length == 0) continue;
                        if (entries.ContainsKey(name)) throw new InvalidDataException("原知识库含有重名附件。");
                        total += entry.Length; if (entry.Length > 512L * 1024 * 1024 || total > MaxTotal + MaxCsv) throw new InvalidDataException("原知识库中的素材超过允许大小。");
                        entries.Add(name, entry);
                    }
                    ZipArchiveEntry csv;
                    if (!entries.TryGetValue("export.csv", out csv) || csv.Length > MaxCsv) throw new InvalidDataException("未找到原知识库的 export.csv，文件格式可能已更新。");
                    string text;
                    using (Stream stream = csv.Open()) using (MemoryStream buffer = new MemoryStream())
                    {
                        LibraryTransfer.CopyLimited(stream, buffer, MaxCsv); buffer.Position = 0; VerifyCrc(buffer, csv, crcs); buffer.Position = 0;
                        using (StreamReader reader = new StreamReader(buffer, new UTF8Encoding(false, true), true)) text = reader.ReadToEnd();
                    }
                    List<List<string>> rows = LibraryTransfer.ParseCsv(text);
                    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                    {
                        List<string> row = rows[rowIndex];
                        if (row.All(String.IsNullOrWhiteSpace)) continue;
                        if (row.Count != 6) throw new InvalidDataException("原知识库含有尚未支持的列结构，已停止导入以避免遗漏内容。");
                        string categoryName = row[0].Trim(); if (categoryName.Length == 0) categoryName = "导入的话术";
                        if (!String.IsNullOrWhiteSpace(row[1])) categoryName += " / " + row[1].Trim();
                        Category category = result.Library.Categories.FirstOrDefault(c => String.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                        if (category == null) { category = new Category { Id = Guid.NewGuid().ToString("N"), Name = categoryName }; result.Library.Categories.Add(category); }
                        string color = row[5].Trim().TrimStart('#'); if (color.Length > 0 && !Regex.IsMatch(color, @"^[0-9a-fA-F]{6}$")) throw new InvalidDataException("原知识库含有无法识别的关键词颜色。");
                        List<string> references = row[4].Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList();
                        Phrase phrase = new Phrase { Id = Guid.NewGuid().ToString("N"), CategoryId = category.Id, Keyword = row[2], ColorHex = color.Length == 0 ? "" : "#" + color.ToUpperInvariant(), Text = row[3].Replace("^J", "\r\n"), InlineAttachments = references.Count > 0 };
                        foreach (string reference in references)
                        {
                            string asset = AssetName(reference); ZipArchiveEntry entry;
                            if (!entries.TryGetValue(asset, out entry)) throw new InvalidDataException("原知识库中缺少引用的图片/文件，已停止导入：" + Path.GetFileName(reference));
                            string relative = "素材/" + asset.Substring("images/".Length);
                            if (!result.Sources.ContainsKey(relative))
                            {
                                string staged = Path.Combine(result.TemporaryFolder, Guid.NewGuid().ToString("N"));
                                using (Stream source = entry.Open()) using (FileStream target = File.Create(staged)) LibraryTransfer.CopyLimited(source, target, entry.Length);
                                if (new FileInfo(staged).Length != entry.Length) throw new InvalidDataException("原知识库附件读取不完整。");
                                using (FileStream check = File.OpenRead(staged)) VerifyCrc(check, entry, crcs);
                                result.Sources.Add(relative, staged); result.Hashes.Add(relative, LibraryTransfer.Digest(staged));
                            }
                            string display = reference.Replace('\\', '/'); int query = display.IndexOfAny(new[] { '?', '#' }); if (query >= 0) display = display.Substring(0, query);
                            phrase.Attachments.Add(new Attachment { Name = display.Substring(display.LastIndexOf('/') + 1), RelativePath = relative });
                        }
                        // Some original exports retain image markers after their references were deleted.
                        // Recover available content, but make every removed marker visible before import.
                        List<string> missing = new List<string>(); string originalText = phrase.Text;
                        phrase.Text = PhraseContent.Marker.Replace(phrase.Text, delegate(Match match)
                        {
                            int index;
                            if (Int32.TryParse(match.Groups["index"].Value, out index) && index >= 1 && index <= phrase.Attachments.Count) return match.Value;
                            missing.Add(match.Value); return "";
                        });
                        if (missing.Count > 0)
                        {
                            if (String.IsNullOrWhiteSpace(phrase.Text) && phrase.Attachments.Count == 0) throw new InvalidDataException("原知识库第 " + (rowIndex + 1) + " 条只包含失效图片标记，没有可恢复的文字或附件。");
                            result.Warnings.Add(new ImportWarning { PhraseId = phrase.Id, OriginalText = originalText,
                                Description = "原文件第 " + (rowIndex + 1) + " 条 · 分类「" + categoryName + "」：现有 " + phrase.Attachments.Count + " 个附件，已移除没有对应附件的标记 " + String.Join("、", missing) + "。" });
                        }
                        result.Library.Phrases.Add(phrase);
                    }
                    if (result.Library.Phrases.Count == 0) throw new InvalidDataException("原知识库没有可导入的话术。");
                    LocalStore.Validate(result.Library);
                }
                File.Delete(decoded); return result;
            }
            catch (CryptographicException) { result.Dispose(); throw new InvalidDataException(".bangkefu 解密失败。文件可能损坏，或采用了尚未支持的版本。"); }
            catch { result.Dispose(); throw; }
        }
    }
}
