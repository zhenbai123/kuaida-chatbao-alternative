using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using QuickReplyLite;

internal static class ExcelTests
{
    internal static void Run(string folder, Action<bool,string> check)
    {
        Directory.CreateDirectory(folder); string file=Path.Combine(folder,"example.xlsx");
        var rows=new List<string[]> { new[]{"聊天宝快捷回复：说明文字"}, new[]{"一级分类（必填）","二级分类","关键词","话术（必填）","图片","背景色"}, new[]{"售后","退货","答复","第一行\n第二行 😀","","3A84FF"}, new[]{"售前","","介绍","真的很实惠","","FFFFFF"} };
        Write(file,rows);
        using(var imported=LibraryTransfer.Load(file))
        {
            check(imported.Library.Phrases.Count==2,"Excel skips banner and recognizes required-header aliases");
            check(imported.Library.Phrases[0].Text==rows[2][3] && imported.Library.Phrases[0].Keyword=="答复","Excel preserves Unicode, emoji and body line breaks");
            check(imported.Library.Categories[0].Name=="售后 / 退货" && imported.Library.Phrases[0].ColorHex=="#3A84FF" && imported.Library.Phrases[1].ColorHex=="#FFFFFF","Excel preserves hierarchy and non-palette RGB colors");
            check(!imported.HasBoardLayout && imported.FileCount==0,"Excel without board column uses selectable destination board");
        }
        rows[2][4]="missing.png";Write(file,rows);
        using(var imported=LibraryTransfer.Load(file))
        {
            check(imported.Library.Phrases.Count==2 && imported.FileCount==0 && imported.Library.Phrases[0].Text==rows[2][3],"Excel missing image references no longer block text import");
            check(imported.Warnings.Count==1 && imported.Warnings[0].OriginalReferences.Contains("missing.png") && imported.Warnings[0].Description.Contains("第 3 行"),"Excel records exact source row and missing references for manual recovery");
        }
        rows[2][4]="";
        MissingMedia(folder,check);
        rows[2][5]="not-a-color";Write(file,rows);Reject(file,check,"Excel invalid colors reject the import");rows[2][5]="3A84FF";
        rows[2][3]=new string('文',50001);Write(file,rows);Reject(file,check,"Excel oversized bodies reject before store mutation");
        Write(file,new List<string[]> {new[]{"未知格式","未知列"},new[]{"数据","内容值"}});Reject(file,check,"Excel unknown headers report an error");
        File.WriteAllText(file,"corrupt");Reject(file,check,"Excel corrupt input reports an error");
        Write(file,new List<string[]> {new[]{"板块","分类","关键词","话术内容","关键词底色"},new[]{"部门","常用","你好","正文","#123456"}});
        using(var imported=LibraryTransfer.Load(file))check(imported.HasBoardLayout && imported.Library.Boards.Any(b=>b.Name=="部门"),"Excel explicit board layout is preserved");
        // Same ZIP stream with .xls extension verifies content-based reader selection.
        string renamed=Path.Combine(folder,"renamed.xls");File.Copy(file,renamed);
        using(var imported=LibraryTransfer.Load(renamed))check(imported.Library.Phrases[0].Text=="正文","Excel reader detects container independently of filename extension");
    }
    static void MissingMedia(string folder,Action<bool,string> check)
    {
        string file=Path.Combine(folder,"missing-media.xlsx");
        string original=" 前文\n#图1#中间 https://example.test/a.png #文件2#后文 😀 ";
        var rows=new List<string[]> {new[]{"说明"},new[]{"分类","关键词","话术","图片","文件","底色"},
            new[]{"常用","混排",original,"https://example.test/missing.png","C:\\missing.txt","9C27B0"},
            new[]{"常用","只有图片","#图片1##图99999999999999#","missing.png","","FFFFFF"},
            new[]{"常用","只有文件","","","missing.pdf",""},
            new[]{"常用","无引用标记","正文#附件1#结尾","","","FFFFFF"},
            new[]{"常用","正常","普通文字，没有附件","","","FFFFFF"}};
        Write(file,rows);
        using(var incoming=LibraryTransfer.Load(file))
        {
            check(incoming.Library.Phrases.Count==3 && incoming.SkippedSourceRows==2 && incoming.Warnings.Count==4,"Excel skips media-only rows and continues all usable text rows");
            var first=incoming.Library.Phrases[0];
            check(first.Text==" 前文\n中间 https://example.test/a.png 后文 😀 " && first.Keyword=="混排" && first.ColorHex=="#9C27B0" && first.Attachments.Count==0 && !first.InlineAttachments,"Excel strips only media markers while preserving exact text, whitespace, URLs and metadata");
            check(incoming.Library.Phrases[1].Text=="正文结尾" && incoming.Library.Phrases[2].Text==rows[6][2],"Excel handles dangling markers without references and keeps unaffected text intact");
            check(incoming.WarningReport().Contains(original) && incoming.WarningReport().Contains("missing.pdf") && incoming.WarningReport().Contains("只有图片"),"Excel report retains originals and references, including skipped media-only rows");
            var store=new LocalStore(Path.Combine(folder,"missing-target"));
            var result=LibraryTransfer.Import(store,incoming,false,true,Board.PublicId);
            check(result.Added==3 && File.Exists(result.ReportPath) && File.ReadAllText(result.ReportPath).Contains("C:\\missing.txt"),"text-first import commits usable rows and a missing-media report");
            byte[] saved=File.ReadAllBytes(store.FilePath);
            var repeat=LibraryTransfer.Import(store,incoming,false,true,Board.PublicId);
            check(repeat.Added==0 && repeat.Skipped==3 && saved.SequenceEqual(File.ReadAllBytes(store.FilePath)),"repeating a text-first import skips duplicates without changing existing data");
            var reopened=new LocalStore(store.Folder);
            check(reopened.Data.Phrases.Any(p=>p.Text==first.Text && p.ColorHex==first.ColorHex),"text-first import survives reopening with text and colors intact");
        }
        Write(file,new List<string[]> {rows[1],rows[3],rows[4]});
        Reject(file,check,"a table containing only unavailable media reports no usable text instead of making empty phrases");
        string csv=Path.Combine(folder,"missing-media.csv");
        File.WriteAllText(csv,"分类,关键词,话术,图片,文件,背景色\r\n常用,示例,前#图1#后,missing.png,missing.pdf,3A84FF\r\n",new UTF8Encoding(true));
        using(var incoming=LibraryTransfer.Load(csv))check(incoming.Library.Phrases.Single().Text=="前后" && incoming.Warnings.Count==1 && incoming.Warnings[0].OriginalReferences.Contains("missing.pdf"),"CSV uses the same nonblocking missing-media recovery and report");
    }
    static void Reject(string path,Action<bool,string> check,string name){bool rejected=false;try{using(var r=LibraryTransfer.Load(path)) {}}catch(InvalidDataException){rejected=true;}check(rejected,name);}
    static void Write(string file,List<string[]> rows)
    {
        using(var zip=new ZipArchive(File.Create(file),ZipArchiveMode.Create))
        {
            Add(zip,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>");
            Add(zip,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip,"xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"知识库\" sheetId=\"1\" r:id=\"r1\"/></sheets></workbook>");
            Add(zip,"xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var xml=new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for(int i=0;i<rows.Count;i++){xml.Append("<row r=\"").Append(i+1).Append("\">");for(int c=0;c<rows[i].Length;c++)xml.Append("<c r=\"").Append((char)('A'+c)).Append(i+1).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(System.Security.SecurityElement.Escape(rows[i][c])).Append("</t></is></c>");xml.Append("</row>");}
            xml.Append("</sheetData></worksheet>");Add(zip,"xl/worksheets/sheet1.xml",xml.ToString());
        }
    }
    static void Add(ZipArchive zip,string path,string text){using(var w=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false)))w.Write(text);}
}
