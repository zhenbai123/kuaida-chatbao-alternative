using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ExcelDataReader;

namespace QuickReplyLite
{
    internal static class ExcelLibraryReader
    {
        internal static ImportedLibrary Load(string path)
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Excel 文件最多20 MB。");
            ImportedLibrary result = new ImportedLibrary { Library = new Document() };
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration()))
                {
                    int scanned = 0; long characters = 0;
                    do
                    {
                        List<List<string>> rows = new List<List<string>>(); bool found = false, nonempty = false;
                        List<int> rowNumbers = new List<int>(); int sheetRow = 0;
                        while (reader.Read())
                        {
                            sheetRow++;
                            if (++scanned > 20000 || reader.FieldCount > 40) throw new InvalidDataException("Excel 超过20000行或40列的读取限制。");
                            List<string> row = new List<string>();
                            for (int c = 0; c < reader.FieldCount; c++)
                            {
                                string value = Convert.ToString(reader.GetValue(c), CultureInfo.InvariantCulture) ?? "";
                                characters += value.Length;
                                if (characters > 20 * 1024 * 1024) throw new InvalidDataException("Excel 文字内容超过20 MB。");
                                row.Add(value);
                            }
                            if (row.All(String.IsNullOrWhiteSpace)) continue;
                            nonempty = true;
                            if (!found)
                            {
                                if (!LibraryTransfer.IsTableHeader(row)) continue;
                                found = true;
                            }
                            rows.Add(row);
                            rowNumbers.Add(sheetRow);
                        }
                        if (!found && nonempty) throw new InvalidDataException("工作表「" + reader.Name + "」未找到话术表头，需要“话术”或“话术内容”等正文列。");
                        if (found) LibraryTransfer.AppendTable(result, rows, "工作表「" + reader.Name + "」", rowNumbers);
                    } while (reader.NextResult());
                }
                if (result.Library.Phrases.Count == 0) throw new InvalidDataException("Excel 中没有可导入的话术。");
                LocalStore.Validate(result.Library); return result;
            }
            catch (InvalidDataException) { result.Dispose(); throw; }
            catch (Exception ex)
            {
                result.Dispose();
                throw new InvalidDataException("Excel 读取失败，请检查文件是否损坏或设置了密码。可用 Excel/WPS 重新另存为 .xls 或 .xlsx 后导入。", ex);
            }
        }
    }
}
