using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QuickReplyLite
{
    internal static class DialogSizing
    {
        internal static void FitWorkingArea(Form form)
        {
            Rectangle area = Screen.FromControl(form).WorkingArea;
            int width = Math.Max(300, area.Width - 24), height = Math.Max(260, area.Height - 24);
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
            form.Size = new Size(Math.Min(form.Width, width), Math.Min(form.Height, height));
            form.Location = new Point(Math.Max(area.Left, Math.Min(form.Left, area.Right - form.Width)), Math.Max(area.Top, Math.Min(form.Top, area.Bottom - form.Height)));
        }
    }

    internal sealed class ImagePreviewForm : Form
    {
        private readonly Bitmap image;
        internal static bool CanPreview(string name)
        {
            string extension = Path.GetExtension(name ?? "").ToLowerInvariant();
            return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" }.Contains(extension);
        }

        private static void Orient(Image image)
        {
            try
            {
                if (!image.PropertyIdList.Contains(0x112)) return;
                int orientation = image.GetPropertyItem(0x112).Value[0];
                RotateFlipType[] transforms = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone,
                    RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX,
                    RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                if (orientation >= 2 && orientation <= 8) image.RotateFlip(transforms[orientation]);
            }
            catch (ArgumentException) { }
        }

        internal static Bitmap LoadThumbnail(string path, int maxWidth, int maxHeight)
        {
            using (Image original = Image.FromFile(path))
            {
                Orient(original);
                double ratio = Math.Min(1, Math.Min((double)maxWidth / original.Width, (double)maxHeight / original.Height));
                Bitmap thumb = new Bitmap(Math.Max(1, (int)(original.Width * ratio)), Math.Max(1, (int)(original.Height * ratio)));
                try
                {
                    using (Graphics graphics = Graphics.FromImage(thumb))
                    { graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(original, 0, 0, thumb.Width, thumb.Height); }
                    return thumb;
                }
                catch { thumb.Dispose(); throw; }
            }
        }

        internal ImagePreviewForm(string path, string name)
        {
            using (Image original = Image.FromFile(path)) { Orient(original); image = new Bitmap(original); }
            Text = "图片预览 · " + name; Icon = Program.ApplicationIcon; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Size = new Size(900, 700); MinimumSize = new Size(450, 350); StartPosition = FormStartPosition.CenterParent;
            Panel canvas = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(238, 240, 244) };
            PictureBox picture = new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = canvas.BackColor };
            canvas.Controls.Add(picture);
            FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 4), WrapContents = false };
            Button fit = new Button { Text = "适应窗口", AutoSize = true };
            Button actual = new Button { Text = "原始大小", AutoSize = true };
            Label size = new Label { Text = image.Width + " × " + image.Height + " 像素", AutoSize = true, Margin = new Padding(14, 6, 0, 0) };
            fit.Click += delegate { canvas.AutoScrollPosition = Point.Empty; picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; };
            actual.Click += delegate { picture.Dock = DockStyle.None; picture.Location = Point.Empty; picture.SizeMode = PictureBoxSizeMode.Normal; picture.Size = image.Size; };
            bar.Controls.AddRange(new Control[] { fit, actual, size }); Controls.Add(canvas); Controls.Add(bar);
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate { DialogSizing.FitWorkingArea(this); };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && image != null) image.Dispose();
        }
    }

    internal sealed class BatchReplaceForm : Form
    {
        private readonly LocalStore store;
        private readonly TextBox find = new TextBox();
        private readonly TextBox replacement = new TextBox();
        private readonly ListBox matches = new ListBox();
        private readonly RichTextBox before = new RichTextBox();
        private readonly RichTextBox after = new RichTextBox();
        private readonly Label summary = new Label();
        private readonly Button apply = new Button();
        private List<TextReplacement> preview = new List<TextReplacement>();
        private bool searched;
        internal int ChangedCount { get; private set; }

        internal BatchReplaceForm(LocalStore value)
        {
            store = value; Text = "批量修改话术"; Icon = Program.ApplicationIcon; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Size = new Size(900, 720); MinimumSize = new Size(700, 580); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "查找所有分类的正文，精确匹配文字；关键词、图片和文件保持不变。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            Button search = new Button { Text = "查找 / 预览", Dock = DockStyle.Fill };
            search.Click += delegate { searched = true; RenderPreview(); };
            find.MaxLength = 50000; replacement.MaxLength = 50000;
            find.AccessibleName = "查找正文中的文字"; replacement.AccessibleName = "替换为，可留空删除匹配文字";
            layout.Controls.Add(InputRow("查找文字", find, search), 0, 1);
            layout.Controls.Add(InputRow("替换为", replacement, new Label { Text = "留空可删除匹配文字", TextAlign = ContentAlignment.MiddleLeft }), 0, 2);
            find.TextChanged += delegate { searched = false; ClearPreview("请输入查找文字，点击“查找 / 预览”。"); };
            replacement.TextChanged += delegate { if (searched) RenderPreview(); };
            find.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { searched = true; RenderPreview(); e.SuppressKeyPress = true; } };
            matches.Dock = DockStyle.Fill; matches.IntegralHeight = false; matches.HorizontalScrollbar = true; matches.AccessibleName = "匹配的话术，选择后可比较原文和替换结果";
            matches.SelectedIndexChanged += delegate { ShowComparison(); }; layout.Controls.Add(matches, 0, 3);
            TableLayoutPanel comparison = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); comparison.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            comparison.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); comparison.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            comparison.Controls.Add(new Label { Text = "原文（黄色为匹配文字）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            comparison.Controls.Add(new Label { Text = "替换后（绿色为替换文字）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            foreach (RichTextBox box in new[] { before, after }) { box.Dock = DockStyle.Fill; box.ReadOnly = true; box.BackColor = Color.White; box.DetectUrls = false; box.WordWrap = true; }
            before.AccessibleName = "替换前的完整正文"; after.AccessibleName = "替换后的完整正文";
            comparison.Controls.Add(before, 0, 1); comparison.Controls.Add(after, 1, 1); layout.Controls.Add(comparison, 0, 4);
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 9, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft; summary.AutoEllipsis = true;
            apply.Text = "替换全部匹配话术"; apply.Dock = DockStyle.Fill; apply.Enabled = false; apply.Click += delegate { Apply(); };
            Button close = new Button { Text = "关闭", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(summary, 0, 0); footer.Controls.Add(apply, 1, 0); footer.Controls.Add(close, 2, 0); layout.Controls.Add(footer, 0, 5);
            CancelButton = close; ClearPreview("请输入查找文字，点击“查找 / 预览”。"); Shown += delegate { DialogSizing.FitWorkingArea(this); find.Focus(); };
        }

        private Control InputRow(string label, TextBox input, Control action)
        {
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            input.Dock = DockStyle.Fill; action.Dock = DockStyle.Fill; row.Controls.Add(input, 1, 0); row.Controls.Add(action, 2, 0); return row;
        }

        private void ClearPreview(string message)
        {
            preview.Clear(); matches.Items.Clear(); before.Clear(); after.Clear(); apply.Enabled = false; summary.Text = message;
        }

        private void RenderPreview()
        {
            if (find.Text.Length == 0) { ClearPreview("查找文字不能为空。"); return; }
            try { preview = store.PreviewTextReplacement(find.Text, replacement.Text); }
            catch (Exception error) { ClearPreview(error.Message); return; }
            matches.BeginUpdate(); matches.Items.Clear();
            foreach (TextReplacement item in preview)
            {
                Phrase phrase = store.Data.Phrases.First(p => p.Id == item.PhraseId);
                string text = item.Before.Replace("\r", " ").Replace("\n", " "); if (text.Length > 150) text = text.Substring(0, 150) + "…";
                matches.Items.Add("[" + item.Occurrences + "处] " + (phrase.Keyword.Length == 0 ? "" : phrase.Keyword + " · ") + text);
            }
            matches.EndUpdate();
            int changed = preview.Count(p => p.Before != p.After);
            int count = preview.Sum(p => p.Occurrences);
            summary.Text = preview.Count == 0 ? "没有匹配的话术。" : changed == 0 ? "查找词与替换词相同，无需修改。" : "匹配 " + preview.Count + " 条，共 " + count + " 处；保存前自动备份。";
            apply.Text = "替换全部 " + changed + " 条"; apply.Enabled = changed > 0;
            if (preview.Count > 0) matches.SelectedIndex = 0; else { before.Clear(); after.Clear(); }
        }

        private void ShowComparison()
        {
            int index = matches.SelectedIndex;
            if (index < 0 || index >= preview.Count) return;
            TextReplacement item = preview[index]; before.Text = item.Before; after.Text = item.After;
            foreach (RichTextBox box in new[] { before, after }) { box.SelectAll(); box.SelectionBackColor = Color.White; }
            int position = 0, offset = 0, found, highlighted = 0;
            // RichTextBox normalizes CRLF. Use its displayed text for highlight positions.
            string displayed = before.Text;
            string term = find.Text.Replace("\r\n", "\n");
            string inserted = replacement.Text.Replace("\r\n", "\n");
            if (term.Length == 0) return;
            while ((found = displayed.IndexOf(term, position, StringComparison.Ordinal)) >= 0 && highlighted++ < 500)
            {
                before.Select(found, term.Length); before.SelectionBackColor = Color.FromArgb(255, 238, 155);
                if (inserted.Length > 0) { after.Select(found + offset, inserted.Length); after.SelectionBackColor = Color.FromArgb(195, 240, 205); }
                offset += inserted.Length - term.Length; position = found + term.Length;
            }
            before.Select(0, 0); after.Select(0, 0); before.ScrollToCaret(); after.ScrollToCaret();
        }

        private void Apply()
        {
            try
            {
                int changed = store.ApplyTextReplacements(preview); ChangedCount += changed;
                searched = false; ClearPreview("已修改 " + changed + " 条；已保留替换前备份。请重新查找以继续修改。");
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "批量修改未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
}
