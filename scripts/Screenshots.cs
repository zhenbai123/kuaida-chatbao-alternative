using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QuickReplyLite
{
    // Runs the real forms with synthetic data. Never calls Program.Main, sends input,
    // registers startup, creates shortcuts, or reads a user's existing Data directory.
    internal static class ScreenshotProgram
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRectangle { internal int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRectangle rectangle, int size);

        [STAThread]
        private static int Main(string[] args)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "kuaida-screenshots-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string output = args.Length == 0 ? Path.GetFullPath("docs/screenshots") : args[0];
                Directory.CreateDirectory(output);
                LocalStore store = Demo(Path.Combine(temporary, "Data"));
                using (MainForm main = new MainForm(store)) Capture(main, output, "main");
                using (PhraseEditor editor = new PhraseEditor(store, store.Data.Phrases[1], store.Data.Categories[0].Id))
                {
                    editor.Show(); Pump(); Field<TextBox>(editor, "body").Select(0, 0);
                    Capture(editor, output, "editor");
                }
                using (BatchReplaceForm batch = new BatchReplaceForm(store))
                {
                    batch.Show(); Pump();
                    Field<TextBox>(batch, "find").Text = "真的";
                    Field<TextBox>(batch, "replacement").Text = "肯定";
                    typeof(BatchReplaceForm).GetField("searched", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(batch, true);
                    typeof(BatchReplaceForm).GetMethod("RenderPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(batch, null);
                    Capture(batch, output, "batch-replace");
                }
                string package = Path.Combine(temporary, "演示话术.quickreply");
                LibraryTransfer.ExportPackage(store, package, "");
                using (ImportedLibrary incoming = LibraryTransfer.Load(package))
                using (ImportLibraryForm import = new ImportLibraryForm(store, incoming, Board.PersonalId)) Capture(import, output, "import");
                Console.WriteLine("Generated four real WinForms window images with demonstration data.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }

        private static T Field<T>(object instance, string name) where T : class
        { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }

        private static void Pump()
        { for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(60); } }

        private static void Capture(Form form, string output, string name)
        {
            if (!form.Visible) form.Show();
            form.Location = new Point(20, 20); form.TopMost = true; form.Activate(); form.Refresh(); Pump();
            // DWM frame bounds exclude invisible resize borders and the desktop behind
            // them. Copy actual on-screen pixels, including native RichTextBox contents.
            NativeRectangle frame;
            if (DwmGetWindowAttribute(form.Handle, 9, out frame, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeRectangle))) != 0)
                throw new InvalidOperationException("Cannot obtain visible window frame for screenshot.");
            Rectangle bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            }
            Console.WriteLine(name + ".png: " + form.Width + " x " + form.Height);
            form.Close(); Pump();
        }

        private static LocalStore Demo(string directory)
        {
            LocalStore store = new LocalStore(directory);
            store.Data.Categories.Clear(); store.Data.Phrases.Clear();
            store.Data.Boards.Add(new Board { Id = "after-sale", Name = "售后" });
            string[] names = { "常用", "产品介绍", "价格咨询", "付款方式", "物流配送", "售后服务", "活动优惠", "退换说明", "合作咨询", "结束语" };
            for (int i = 0; i < names.Length; i++) store.Data.Categories.Add(new Category { Id = "category-" + i, Name = names[i], BoardId = Board.PublicId });
            store.Data.Categories.Add(new Category { Id = "personal-common", Name = "我的常用", BoardId = Board.PersonalId });
            store.Data.Categories.Add(new Category { Id = "after-sale-common", Name = "问题处理", BoardId = "after-sale" });
            string[] keywords = { "欢迎", "产品资料", "价格", "活动", "物流", "付款", "售后", "确认", "感谢" };
            string[] texts = {
                "您好，欢迎咨询！请问有什么可以帮您？",
                "这是产品资料与使用说明，您可以先看一下。\r\n有任何问题都可以随时联系我。",
                "这款产品真的很实惠，具体价格可以根据您的需求确认。",
                "这个优惠真的假的？我帮您核实一下活动规则。",
                "收到订单后会尽快安排发出，发货后会同步物流信息。",
                "支持常见付款方式，请以订单页面显示的信息为准。",
                "请提供问题描述和相关图片，我会帮您跟进处理。",
                "已收到您的信息，我确认后尽快回复您。",
                "感谢您的支持，祝您生活愉快！" };
            string[] colors = { "#2878D0", "#9329AE", "#E64A45", "#4C9F50", "#00A6B2" };
            for (int i = 0; i < texts.Length; i++) store.Data.Phrases.Add(new Phrase { Id = "demo-" + i, CategoryId = "category-" + (i < 4 ? 0 : i), Keyword = keywords[i], ColorHex = colors[i % colors.Length], Text = texts[i] });
            string image = Path.Combine(directory, "产品资料-演示.png");
            using (Bitmap bitmap = new Bitmap(640, 380))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font heading = new Font("Microsoft YaHei UI", 28, FontStyle.Bold))
            using (Font detail = new Font("Microsoft YaHei UI", 17))
            {
                graphics.Clear(Color.FromArgb(237, 245, 255));
                using (Brush accent = new SolidBrush(Color.FromArgb(37, 112, 201)))
                { graphics.FillRectangle(accent, 0, 0, 16, 380); graphics.DrawString("产品资料", heading, accent, 52, 48); }
                graphics.DrawString("本地图片 · 演示附件", detail, Brushes.DimGray, 56, 118);
                graphics.FillRectangle(Brushes.White, 56, 184, 526, 130);
                graphics.DrawString("支持缩略图与放大预览\n图片与话术一起保存到本机", detail, Brushes.SlateGray, 78, 208);
                bitmap.Save(image, ImageFormat.Png);
            }
            store.Data.Phrases[1].Attachments.Add(store.CopyAttachment(image));
            store.Data.Preferences.ActiveBoardId = Board.PublicId;
            store.Data.Preferences.TopMost = true;
            store.Data.Preferences.Width = 400; store.Data.Preferences.Height = 740;
            store.Data.Preferences.CategoryPanelHeight = 164;
            store.Save(); return store;
        }
    }
}
