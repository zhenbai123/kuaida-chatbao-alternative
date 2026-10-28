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
        private sealed class SequenceItem
        {
            internal DeliveryStep Step;
            internal string Label;
            public override string ToString() { return Label; }
        }
        private readonly LocalStore store;
        private Phrase original;
        private readonly ComboBox category = new ComboBox();
        private readonly TextBox keyword = new TextBox();
        private readonly TextBox body = new TextBox();
        private readonly ListBox files = new ListBox();
        private readonly Label hint = new Label();
        private readonly FlowLayoutPanel colors = new FlowLayoutPanel();
        private readonly CheckBox ordered = new CheckBox();
        private readonly ListBox sequence = new ListBox();
        private readonly Label sequenceTitle = new Label();
        private readonly Label sequenceNote = new Label();
        private readonly ToolTip compositionTips = new ToolTip();
        private bool loadingDraft;
        private bool inlineMode;
        private readonly Label bodyLabel = new Label();
        private string colorHex = "#2878D0";
        private readonly string[] palette = { "", "#2878D0", "#4355AF", "#00A6B2", "#4C9F50", "#F3AB16", "#E64A45", "#9329AE", "#242424" };

        public PhraseEditor(LocalStore value, Phrase phrase, string selectedCategory)
        {
            store = value; original = phrase; inlineMode = phrase == null || phrase.InlineAttachments; loadingDraft = true;
            Text = phrase == null ? "新建话术" : "修改话术";
            Icon = Program.ApplicationIcon;
            Font = new Font("Microsoft YaHei UI", 9F); Size = new Size(920, 730); MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; BackColor = Color.FromArgb(248, 249, 251);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 12), ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
            TableLayoutPanel orderBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            orderBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); orderBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ordered.Text = "按正文位置逐条发送文字、图片和文件"; ordered.AutoSize = true; ordered.Checked = inlineMode;
            ordered.AccessibleName = "按正文位置逐条发送";
            ordered.CheckedChanged += delegate
            {
                if (!ordered.Checked && inlineMode && !loadingDraft)
                {
                    loadingDraft = true;
                    try { body.Text = PhraseContent.PlainText(DraftPhrase()); hint.Text = "已取消穿插位置，改为先发整段文字，再发送全部附件。"; }
                    finally { loadingDraft = false; }
                }
                inlineMode = ordered.Checked; UpdateSequence();
            };
            orderBar.Controls.Add(ordered, 0, 0);
            bodyLabel.Text = "在文字间插入附件；也可剪切标记调整位置。";
            bodyLabel.Dock = DockStyle.Fill; bodyLabel.TextAlign = ContentAlignment.MiddleLeft; orderBar.Controls.Add(bodyLabel, 0, 1); layout.Controls.Add(orderBar, 0, 2);
            body.Multiline = true; body.AcceptsReturn = true; body.ScrollBars = ScrollBars.Vertical; body.MaxLength = 50000; body.Dock = DockStyle.Fill;
            body.AccessibleName = "话术文字，可在光标处插入图片或文件";
            body.TextChanged += delegate { UpdateSequence(); };
            TableLayoutPanel composition = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            composition.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); composition.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            composition.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); composition.Controls.Add(body, 0, 0);
            TableLayoutPanel previewPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(8, 0, 0, 0) };
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            sequenceTitle.Dock = DockStyle.Fill; sequenceTitle.TextAlign = ContentAlignment.MiddleLeft;
            sequence.Dock = DockStyle.Fill; sequence.IntegralHeight = false; sequence.DrawMode = DrawMode.OwnerDrawFixed; sequence.ItemHeight = 64;
            sequence.AccessibleName = "发送顺序预览，从上到下依次发送"; sequence.DrawItem += DrawSequence;
            sequenceNote.Dock = DockStyle.Fill; sequenceNote.ForeColor = Color.DimGray;
            previewPanel.Controls.Add(sequenceTitle, 0, 0); previewPanel.Controls.Add(sequence, 0, 1); previewPanel.Controls.Add(sequenceNote, 0, 2);
            composition.Controls.Add(previewPanel, 1, 0); layout.Controls.Add(composition, 0, 3);

            FlowLayoutPanel fileBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            Button add = new Button { Text = "＋ 图片 / 文件", AutoSize = true, Height = 27 };
            Button remove = new Button { Text = "移除选中", AutoSize = true, Height = 27 };
            Button preview = new Button { Text = "预览图片", AutoSize = true, Height = 27, Enabled = false };
            Button insert = new Button { Text = "插入到光标处", AutoSize = true, Height = 27, Enabled = false };
            Label fileNote = new Label { Text = "最多9个，支持拖入", AutoSize = true, Margin = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
            add.Click += delegate { using (OpenFileDialog dialog = new OpenFileDialog { Multiselect = true, Title = "添加本地图片或文件", CheckFileExists = true, Filter = "所有文件|*.*|图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" }) if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames); };
            remove.Click += delegate
            {
                if (files.SelectedIndex < 0) return;
                int index = files.SelectedIndex;
                if (inlineMode) body.Text = PhraseContent.RemoveAttachmentMarker(body.Text, index + 1);
                ((DraftFile)files.Items[index]).Dispose(); files.Items.RemoveAt(index);
                if (files.Items.Count > 0) files.SelectedIndex = Math.Min(index, files.Items.Count - 1);
                UpdateSequence();
            };
            preview.Click += delegate { PreviewSelectedImage(); };
            insert.Click += delegate { InsertSelectedAttachment(); };
            compositionTips.SetToolTip(insert, "先在正文点击插入位置，再选择下面的附件。已有该附件标记时移到光标处；需要重复发送时可复制标记。");
            compositionTips.SetToolTip(ordered, "勾选后，箭头发送会按右侧顺序逐条发送。取消会移除正文中的位置标记，改为整段文字和附件。");
            fileBar.Controls.AddRange(new Control[] { add, insert, remove, preview, fileNote }); layout.Controls.Add(fileBar, 0, 4);
            files.Dock = DockStyle.Fill; files.IntegralHeight = false; files.AccessibleName = "关联图片和文件，可查看缩略图并双击预览";
            files.DrawMode = DrawMode.OwnerDrawFixed; files.ItemHeight = 64; files.DrawItem += DrawFile;
            files.SelectedIndexChanged += delegate { DraftFile file = files.SelectedItem as DraftFile; preview.Enabled = file != null && ImagePreviewForm.CanPreview(file.Name); insert.Enabled = file != null; };
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
            saveNew.Click += delegate { if (SavePhrase()) { loadingDraft = true; original = null; inlineMode = true; ordered.Checked = true; keyword.Clear(); body.Clear(); ClearFiles(); loadingDraft = false; UpdateSequence(); Text = "新建话术"; hint.Text = "已保存，可继续新建。"; body.Focus(); } };
            actions.Controls.AddRange(new Control[] { saveNew, save, cancel }); footer.Controls.Add(actions, 1, 0); layout.Controls.Add(footer, 0, 6);
            CancelButton = cancel;
            if (phrase != null)
            {
                keyword.Text = phrase.Keyword; body.Text = phrase.Text; colorHex = phrase.ColorHex ?? "";
                foreach (Attachment item in phrase.Attachments) files.Items.Add(new DraftFile { Name = item.Name, Existing = item });
            }
            loadingDraft = false; UpdateSequence();
            Shown += delegate { DialogSizing.FitWorkingArea(this); RenderColors(); files.ItemHeight = sequence.ItemHeight = (int)Math.Round(64 * DeviceDpi / 96.0); if (files.Items.Count > 0) files.SelectedIndex = 0; body.Focus(); };
            FormClosed += delegate { ClearFiles(); compositionTips.Dispose(); };
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
            TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ". " + file.Name, Font, name, Color.FromArgb(35, 35, 35), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            Rectangle note = new Rectangle(left, name.Bottom, name.Width, Font.Height + (int)(8 * scale));
            string marker = "#" + (ImagePreviewForm.CanPreview(file.Name) ? "图" : "文件") + (e.Index + 1) + "#";
            TextRenderer.DrawText(e.Graphics, marker + (image != null ? " · 双击放大预览" : ImagePreviewForm.CanPreview(file.Name) ? " · 图片缺失或无法预览" : " · 按原文件发送"), Font, note, Color.DimGray, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
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

        private Phrase DraftPhrase()
        {
            return new Phrase { Text = body.Text, InlineAttachments = inlineMode,
                Attachments = files.Items.Cast<DraftFile>().Select(f => new Attachment { Name = f.Name }).ToList() };
        }

        private void UpdateSequence()
        {
            if (loadingDraft || IsDisposed) return;
            sequence.BeginUpdate(); sequence.Items.Clear();
            try
            {
                Phrase draft = DraftPhrase(); List<DeliveryStep> steps = DeliveryPlan.Build(draft, DeliveryMode.Send);
                foreach (DeliveryStep step in steps)
                {
                    string label = step.AttachmentIndices.Length == 0 ? "文字：" + step.Text : String.Join("、", step.AttachmentIndices.Select(i => ((DraftFile)files.Items[i]).Name));
                    sequence.Items.Add(new SequenceItem { Step = step, Label = (sequence.Items.Count + 1) + ". " + label });
                }
                sequenceTitle.Text = "发送顺序预览 · " + steps.Count + " 步"; sequenceNote.ForeColor = Color.DimGray;
                if (!inlineMode) sequenceNote.Text = "先发整段文字，再一起发送附件。勾选上方选项后可穿插发送。";
                else
                {
                    HashSet<int> placed = new HashSet<int>();
                    foreach (System.Text.RegularExpressions.Match match in PhraseContent.Marker.Matches(body.Text)) placed.Add(Int32.Parse(match.Groups["index"].Value));
                    int unplaced = files.Items.Count - placed.Count;
                    sequenceNote.Text = (unplaced > 0 ? unplaced + " 个未插入的附件排在最后。" : "从上到下依次发送。") + "主窗口需勾选「回车发送」；双击只粘贴，不分段。";
                }
            }
            catch (Exception error) { sequenceTitle.Text = "发送顺序有误"; sequenceNote.ForeColor = Color.DarkRed; sequenceNote.Text = error.Message; }
            finally { sequence.EndUpdate(); }
        }

        private void DrawSequence(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= sequence.Items.Count) return;
            DeliveryStep step = ((SequenceItem)sequence.Items[e.Index]).Step;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.FillRectangle(selected ? SystemBrushes.Highlight : Brushes.White, e.Bounds);
            Color ink = selected ? SystemColors.HighlightText : Color.FromArgb(35, 35, 35);
            int pad = (int)(6 * DeviceDpi / 96F), numberWidth = (int)(29 * DeviceDpi / 96F);
            Rectangle number = new Rectangle(e.Bounds.Left + pad, e.Bounds.Top + pad, numberWidth, e.Bounds.Height - 2 * pad);
            TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString("00"), Font, number, selected ? ink : Color.FromArgb(40, 120, 208), TextFormatFlags.NoPrefix);
            Rectangle content = new Rectangle(number.Right, e.Bounds.Top + pad, Math.Max(1, e.Bounds.Right - number.Right - pad), e.Bounds.Height - 2 * pad);
            if (step.AttachmentIndices.Length == 0)
                TextRenderer.DrawText(e.Graphics, "文字\r\n" + PreviewText.Wrap(step.Text, Math.Max(5, content.Width / Font.Height), 2), Font, content, ink, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            else
            {
                int index = step.AttachmentIndices[0]; DraftFile file = (DraftFile)files.Items[index]; Bitmap image = file.Thumbnail(store);
                Rectangle thumbnail = new Rectangle(content.Left, content.Top, content.Height, content.Height);
                e.Graphics.FillRectangle(Brushes.WhiteSmoke, thumbnail);
                if (image != null)
                {
                    double ratio = Math.Min((double)thumbnail.Width / image.Width, (double)thumbnail.Height / image.Height);
                    int width = Math.Max(1, (int)(image.Width * ratio)), height = Math.Max(1, (int)(image.Height * ratio));
                    e.Graphics.DrawImage(image, thumbnail.Left + (thumbnail.Width - width) / 2, thumbnail.Top + (thumbnail.Height - height) / 2, width, height);
                }
                else TextRenderer.DrawText(e.Graphics, "文件", Font, thumbnail, Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Rectangle description = new Rectangle(thumbnail.Right + pad, content.Top, Math.Max(1, content.Right - thumbnail.Right - pad), content.Height);
                string kind = step.AttachmentIndices.Length > 1 ? "一起发送 " + step.AttachmentIndices.Length + " 个附件" : (ImagePreviewForm.CanPreview(file.Name) ? "图片 " : "文件 ") + (index + 1);
                TextRenderer.DrawText(e.Graphics, kind + "\r\n" + file.Name, Font, description, ink, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
            using (Pen line = new Pen(Color.FromArgb(230, 233, 238))) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (selected) e.DrawFocusRectangle();
        }

        private void InsertSelectedAttachment()
        {
            if (files.SelectedIndex < 0) return;
            try
            {
                int index = files.SelectedIndex, caret;
                DraftFile file = (DraftFile)files.Items[index];
                string text = PhraseContent.PlaceAttachment(body.Text, body.SelectionStart, index + 1, ImagePreviewForm.CanPreview(file.Name), out caret);
                if (text.Length > body.MaxLength) throw new InvalidOperationException("正文最多50000字，请缩短后再插入。");
                loadingDraft = true;
                try { ordered.Checked = true; inlineMode = true; body.Text = text; body.Select(caret, 0); }
                finally { loadingDraft = false; UpdateSequence(); }
                hint.Text = "已将附件 " + (index + 1) + " 放到光标处；在右侧核对顺序。"; body.Focus();
            }
            catch (Exception error) { hint.Text = error.Message; }
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
                string text = body.Text; int caret = body.SelectionStart, next = files.Items.Count;
                if (inlineMode)
                    foreach (string file in incoming) text = PhraseContent.PlaceAttachment(text, caret, ++next, ImagePreviewForm.CanPreview(file), out caret);
                if (text.Length > body.MaxLength) throw new InvalidOperationException("正文最多50000字，请缩短后再添加附件。");
                loadingDraft = true;
                foreach (string file in incoming)
                {
                    files.Items.Add(new DraftFile { Name = Path.GetFileName(file), Source = file });
                }
                if (inlineMode) { body.Text = text; body.Select(caret, 0); }
                if (incoming.Count > 0) files.SelectedIndex = files.Items.Count - 1;
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "添加附件"); }
            finally { loadingDraft = false; UpdateSequence(); }
        }

        private bool SavePhrase()
        {
            if (String.IsNullOrWhiteSpace(body.Text) && files.Items.Count == 0) { hint.Text = "请输入话术文字，或添加图片/文件。"; return false; }
            List<Attachment> copied = new List<Attachment>();
            Phrase previous = original; int previousIndex = previous == null ? -1 : store.Data.Phrases.FindIndex(p => p.Id == previous.Id);
            Phrase updated = null;
            try
            {
                PhraseContent.Build(DraftPhrase());
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
