using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QuickReplyLite
{
    internal sealed class DraftFile : IDisposable
    {
        internal Attachment Existing;
        internal string Source;
        internal string Name;
        private Bitmap thumbnail;
        private bool attemptedThumbnail;
        internal string PathIn(LocalStore store) { return Existing == null ? Source : store.Resolve(Existing); }
        internal Bitmap Thumbnail(LocalStore store)
        {
            if (!attemptedThumbnail)
            {
                attemptedThumbnail = true;
                if (ImagePreviewForm.CanPreview(Name))
                    try { thumbnail = ImagePreviewForm.LoadThumbnail(PathIn(store), 160, 140); } catch { }
            }
            return thumbnail;
        }
        public void Dispose() { if (thumbnail != null) { thumbnail.Dispose(); thumbnail = null; } }
        public override string ToString() { return Name; }
    }

    internal sealed class PhraseEditor : Form
    {
        private readonly LocalStore store;
        private Phrase original;
        private readonly ComboBox category = new ComboBox();
        private readonly TextBox keyword = new TextBox();
        private readonly TextBox body = new TextBox();
        private readonly ListBox files = new ListBox();
        private readonly Label hint = new Label();
        private readonly FlowLayoutPanel colors = new FlowLayoutPanel();
        private bool inlineMode;
        private readonly Label bodyLabel = new Label();
        private string colorHex = "#2878D0";
        private readonly string[] palette = { "", "#2878D0", "#4355AF", "#00A6B2", "#4C9F50", "#F3AB16", "#E64A45", "#9329AE", "#242424" };

        public PhraseEditor(LocalStore value, Phrase phrase, string selectedCategory)
        {
            store = value; original = phrase; inlineMode = phrase != null && phrase.InlineAttachments;
            Text = phrase == null ? "新建话术" : "修改话术";
            Icon = Program.ApplicationIcon;
            Font = new Font("Microsoft YaHei UI", 9F); Size = new Size(720, 700); MinimumSize = new Size(620, 620);
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; BackColor = Color.FromArgb(248, 249, 251);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 12), ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51)); Controls.Add(layout);

            category.DropDownStyle = ComboBoxStyle.DropDownList; category.DisplayMember = "Name"; category.ValueMember = "Id";
            category.DataSource = store.Data.Categories.Select(c => new { c.Id, Name = store.CategoryLabel(c) }).ToList(); category.AccessibleName = "选择板块和分类";
            string chosen = phrase == null ? selectedCategory : phrase.CategoryId;
            category.SelectedValue = store.Data.Categories.Any(c => c.Id == chosen) ? chosen : store.Data.Categories[0].Id;
            layout.Controls.Add(Field("请选择板块 / 分类", category), 0, 0);
            TableLayoutPanel tagRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            tagRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); tagRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            keyword.MaxLength = 80; keyword.AccessibleName = "关键词";
            tagRow.Controls.Add(Field("关键词（可不填）", keyword), 0, 0);
            colors.Dock = DockStyle.Fill; colors.WrapContents = false; colors.AutoScroll = true;
            tagRow.Controls.Add(Field("关键词底色", colors), 1, 0); layout.Controls.Add(tagRow, 0, 1);
            bodyLabel.Text = inlineMode ? "正文中的 #图1# 对应附件1，发送时按标记插入图片/文件" : "话术文字（可以换行，也可以只添加附件）";
            bodyLabel.Dock = DockStyle.Fill; bodyLabel.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(bodyLabel, 0, 2);
            body.Multiline = true; body.AcceptsReturn = true; body.ScrollBars = ScrollBars.Vertical; body.MaxLength = 50000; body.Dock = DockStyle.Fill;
            body.AccessibleName = "话术文字"; layout.Controls.Add(body, 0, 3);

            FlowLayoutPanel fileBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            Button add = new Button { Text = "＋ 图片 / 文件", AutoSize = true, Height = 27 };
            Button remove = new Button { Text = "移除选中", AutoSize = true, Height = 27 };
            Button preview = new Button { Text = "预览图片", AutoSize = true, Height = 27, Enabled = false };
            Label fileNote = new Label { Text = "最多9个，支持拖入", AutoSize = true, Margin = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
            add.Click += delegate { using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Title = "添加本地图片或文件", CheckFileExists = true, Filter = "所有文件|*.*|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" }) if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames); };
            remove.Click += delegate
            {
                if (files.SelectedIndex < 0) return;
                int index = files.SelectedIndex;
                if (inlineMode) body.Text = PhraseContent.RemoveAttachmentMarker(body.Text, index + 1);
                ((DraftFile)files.Items[index]).Dispose(); files.Items.RemoveAt(index);
                if (files.Items.Count > 0) files.SelectedIndex = Math.Min(index, files.Items.Count - 1);
            };
            preview.Click += delegate { PreviewSelectedImage(); };
            fileBar.Controls.AddRange(new Control[] { add, remove, preview, fileNote }); layout.Controls.Add(fileBar, 0, 4);
            files.Dock = DockStyle.Fill; files.IntegralHeight = false; files.AccessibleName = "关联图片和文件，可查看缩略图并双击预览";
            files.DrawMode = DrawMode.OwnerDrawFixed; files.ItemHeight = 64; files.DrawItem += DrawFile;
            files.SelectedIndexChanged += delegate { DraftFile file = files.SelectedItem as DraftFile; preview.Enabled = file != null && ImagePreviewForm.CanPreview(file.Name); };
            files.MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (files.IndexFromPoint(e.Location) >= 0) PreviewSelectedImage(); };
            files.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { PreviewSelectedImage(); e.SuppressKeyPress = true; } };
            layout.Controls.Add(files, 0, 5);
            AllowDrop = true; files.AllowDrop = true;
            DragEventHandler over = delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragEventHandler drop = delegate(object sender, DragEventArgs e) { string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[]; if (dropped != null) AddFiles(dropped); };
            DragEnter += over; DragDrop += drop; files.DragEnter += over; files.DragDrop += drop;

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 12, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            hint.Dock = DockStyle.Fill; hint.AutoEllipsis = true; hint.ForeColor = Color.DimGray; hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "只保存在本机，保存后即可使用。"; footer.Controls.Add(hint, 0, 0);
            FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
            Button saveNew = new Button { Text = "保存并新建", AutoSize = true, Height = 29 };
            Button save = new Button { Text = "保存", Width = 70, Height = 29 };
            Button cancel = new Button { Text = "取消", Width = 70, Height = 29, DialogResult = DialogResult.Cancel };
            foreach (Button action in new[] { saveNew, save, cancel }) action.Margin = new Padding(3, 0, 3, 0);
            save.Click += delegate { if (SavePhrase()) { DialogResult = DialogResult.OK; Close(); } };
            saveNew.Click += delegate { if (SavePhrase()) { original = null; inlineMode = false; bodyLabel.Text = "话术文字（可以换行，也可以只添加附件）"; keyword.Clear(); body.Clear(); ClearFiles(); Text = "新建话术"; hint.Text = "已保存，可继续新建。"; body.Focus(); } };
            actions.Controls.AddRange(new Control[] { saveNew, save, cancel }); footer.Controls.Add(actions, 1, 0); layout.Controls.Add(footer, 0, 6);
            CancelButton = cancel;
            if (phrase != null)
            {
                keyword.Text = phrase.Keyword; body.Text = phrase.Text; colorHex = phrase.ColorHex ?? "";
                foreach (Attachment item in phrase.Attachments) files.Items.Add(new DraftFile { Name = item.Name, Existing = item });
            }
            Shown += delegate { DialogSizing.FitWorkingArea(this); RenderColors(); files.ItemHeight = (int)Math.Round(64 * DeviceDpi / 96.0); if (files.Items.Count > 0) files.SelectedIndex = 0; body.Focus(); };
            FormClosed += delegate { ClearFiles(); };
        }

        private Control Field(string label, Control input)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 0, 10, 4), Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label text = new Label { Text = label, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
            input.Dock = DockStyle.Fill; input.Margin = Padding.Empty;
            panel.Controls.Add(text, 0, 0); panel.Controls.Add(input, 0, 1); return panel;
        }

        private void ClearFiles()
        {
            foreach (DraftFile file in files.Items) file.Dispose();
            files.Items.Clear();
        }

        private void DrawFile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= files.Items.Count) return;
            DraftFile file = (DraftFile)files.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush brush = new SolidBrush(selected ? Color.FromArgb(230, 241, 255) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
            float scale = DeviceDpi / 96F;
            Rectangle thumb = new Rectangle(e.Bounds.Left + (int)(7 * scale), e.Bounds.Top + (int)(6 * scale), (int)(78 * scale), e.Bounds.Height - (int)(12 * scale));
            e.Graphics.FillRectangle(Brushes.WhiteSmoke, thumb);
            Bitmap image = file.Thumbnail(store);
            if (image != null)
            {
                double ratio = Math.Min((double)thumb.Width / image.Width, (double)thumb.Height / image.Height);
                int width = Math.Max(1, (int)(image.Width * ratio)), height = Math.Max(1, (int)(image.Height * ratio));
                e.Graphics.DrawImage(image, thumb.Left + (thumb.Width - width) / 2, thumb.Top + (thumb.Height - height) / 2, width, height);
            }
            else TextRenderer.DrawText(e.Graphics, ImagePreviewForm.CanPreview(file.Name) ? "无预览" : "文件", Font, thumb, Color.Gray, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            int left = thumb.Right + (int)(10 * scale);
            Rectangle name = new Rectangle(left, e.Bounds.Top + (int)(6 * scale), Math.Max(0, e.Bounds.Right - left - 8), Font.Height + (int)(6 * scale));
            TextRenderer.DrawText(e.Graphics, file.Name, Font, name, Color.FromArgb(35, 35, 35), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            Rectangle note = new Rectangle(left, name.Bottom, name.Width, Font.Height + (int)(8 * scale));
            TextRenderer.DrawText(e.Graphics, image != null ? "双击放大预览" : ImagePreviewForm.CanPreview(file.Name) ? "图片缺失或格式无法预览" : "附件将按原文件发送", Font, note, Color.DimGray, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            using (Pen pen = new Pen(Color.FromArgb(230, 233, 238))) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (selected) e.DrawFocusRectangle();
        }

        private void PreviewSelectedImage()
        {
            DraftFile file = files.SelectedItem as DraftFile;
            if (file == null || !ImagePreviewForm.CanPreview(file.Name)) return;
            try { using (ImagePreviewForm preview = new ImagePreviewForm(file.PathIn(store), file.Name)) preview.ShowDialog(this); }
            catch (Exception error) { MessageBox.Show(this, "无法预览图片：" + error.Message, "图片预览"); }
        }

        private void RenderColors()
        {
            foreach (Control old in colors.Controls.Cast<Control>().ToArray()) old.Dispose(); colors.Controls.Clear();
            string[] choices = palette.Contains(colorHex) || String.IsNullOrEmpty(colorHex) ? palette : palette.Concat(new[] { colorHex }).ToArray();
            foreach (string value in choices)
            {
                string chosen = value;
                float scale = DeviceDpi / 96F;
                Button swatch = new Button { Text = value == colorHex ? "✓" : "", Width = (int)(24 * scale), Height = (int)(25 * scale), Margin = new Padding(0, 0, (int)(3 * scale), 0), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                    BackColor = value.Length == 0 ? Color.White : ColorTranslator.FromHtml(value), ForeColor = value.Length == 0 || value == "#F3AB16" ? Color.FromArgb(30, 90, 160) : Color.White };
                swatch.FlatAppearance.BorderColor = Color.FromArgb(170, 185, 200); swatch.AccessibleName = value.Length == 0 ? "无底色" : value;
                swatch.Click += delegate { colorHex = chosen; RenderColors(); }; colors.Controls.Add(swatch);
            }
        }

        private void AddFiles(string[] paths)
        {
            try
            {
                HashSet<string> existing = new HashSet<string>(files.Items.Cast<DraftFile>().Select(f => f.Existing != null ? store.Resolve(f.Existing) : Path.GetFullPath(f.Source)), StringComparer.OrdinalIgnoreCase);
                List<string> incoming = new List<string>();
                foreach (string file in paths)
                {
                    if (!File.Exists(file)) throw new IOException("只能添加文件，不能添加文件夹。");
                    string full = Path.GetFullPath(file); if (existing.Add(full)) incoming.Add(full);
                }
                if (files.Items.Count + incoming.Count > 9) throw new InvalidOperationException("每条话术最多添加9个图片或文件。");
                foreach (string file in incoming)
                {
                    files.Items.Add(new DraftFile { Name = Path.GetFileName(file), Source = file });
                    if (inlineMode) body.AppendText((body.Text.Length == 0 ? "" : Environment.NewLine) + "#" + (ImagePreviewForm.CanPreview(file) ? "图" : "文件") + files.Items.Count + "#");
                }
                if (incoming.Count > 0) files.SelectedIndex = files.Items.Count - 1;
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "添加附件"); }
        }

        private bool SavePhrase()
        {
            if (String.IsNullOrWhiteSpace(body.Text) && files.Items.Count == 0) { hint.Text = "请输入话术文字，或添加图片/文件。"; return false; }
            List<Attachment> copied = new List<Attachment>();
            Phrase previous = original; int previousIndex = previous == null ? -1 : store.Data.Phrases.FindIndex(p => p.Id == previous.Id);
            Phrase updated = null;
            try
            {
                updated = new Phrase { Id = previous == null ? Guid.NewGuid().ToString("N") : previous.Id, CategoryId = (string)category.SelectedValue, Keyword = keyword.Text.Trim(), ColorHex = colorHex, Text = body.Text, InlineAttachments = inlineMode };
                foreach (DraftFile file in files.Items)
                {
                    if (file.Existing != null) updated.Attachments.Add(file.Existing);
                    else { Attachment attachment = store.CopyAttachment(file.Source); copied.Add(attachment); updated.Attachments.Add(attachment); }
                }
                if (previousIndex >= 0) store.Data.Phrases[previousIndex] = updated; else store.Data.Phrases.Add(updated);
                store.Save(); original = updated; return true;
            }
            catch (Exception error)
            {
                if (updated != null) { if (previousIndex >= 0) store.Data.Phrases[previousIndex] = previous; else store.Data.Phrases.Remove(updated); }
                foreach (Attachment item in copied) { try { Directory.Delete(Path.GetDirectoryName(store.Resolve(item)), true); } catch { } }
                hint.Text = "保存失败：" + error.Message; MessageBox.Show(this, error.Message, "保存失败"); return false;
            }
        }
    }
}
