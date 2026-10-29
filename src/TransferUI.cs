using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QuickReplyLite
{
    internal sealed class ImportLibraryForm : Form
    {
        private readonly LocalStore store;
        private readonly ImportedLibrary incoming;
        private readonly RadioButton append = new RadioButton();
        private readonly RadioButton replace = new RadioButton();
        private readonly CheckBox skip = new CheckBox();
        private readonly ListBox list = new ListBox();
        private readonly TextBox content = new TextBox();
        private readonly ComboBox targetBoard = new ComboBox();
        private readonly Label targetHint = new Label();
        internal string TargetBoardId { get { return (string)targetBoard.SelectedValue; } }
        internal bool PreserveOriginalBoards { get { return incoming.HasBoardLayout && String.IsNullOrEmpty(TargetBoardId); } }
        internal ImportResult Result { get; private set; }

        internal ImportLibraryForm(LocalStore local, ImportedLibrary library, string selectedBoard)
        {
            store = local; incoming = library; Text = "导入话术 · 预览"; Font = new Font("Microsoft YaHei UI", 9F);
            Icon = Program.ApplicationIcon;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            Size = new Size(760, 650); MinimumSize = new Size(600, 520); StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, incoming.Warnings.Count > 0 ? 72 : 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(layout);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label summary = new Label { Text = "可导入 " + incoming.Library.Categories.Count + " 个分类、" + incoming.Library.Phrases.Count + " 条话术、" + incoming.FileCount + " 个附件。" + (incoming.SkippedSourceRows == 0 ? "" : "另跳过 " + incoming.SkippedSourceRows + " 行无正文记录。"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            header.Controls.Add(summary, 0, 0); header.SetColumnSpan(summary, 2);
            List<Board> choices = store.Data.Boards.ToList();
            if (incoming.HasBoardLayout) choices.Add(new Board { Id = "", Name = "按文件原有大板块导入" });
            targetBoard.DropDownStyle = ComboBoxStyle.DropDownList; targetBoard.DisplayMember = "Name"; targetBoard.ValueMember = "Id"; targetBoard.DataSource = choices; targetBoard.Dock = DockStyle.Fill; targetBoard.AccessibleName = "选择导入到哪个大板块";
            header.Controls.Add(new Label { Text = "导入到大板块", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); header.Controls.Add(targetBoard, 1, 1);
            targetHint.Dock = DockStyle.Fill; targetHint.TextAlign = ContentAlignment.MiddleLeft; targetHint.ForeColor = Color.DimGray; header.Controls.Add(targetHint, 0, 2); header.SetColumnSpan(targetHint, 2);
            layout.Controls.Add(header, 0, 0);
            FlowLayoutPanel modes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            append.Text = "追加导入：保留现有话术，合并同板块内的同名分类"; append.AutoSize = true; append.Checked = true;
            replace.Text = "替换全部：用导入文件替换当前所有话术"; replace.AutoSize = true;
            modes.Controls.Add(append); modes.Controls.Add(replace); layout.Controls.Add(modes, 0, 1);
            skip.Text = "追加时跳过内容、关键词、底色及附件都相同的话术"; skip.AutoSize = true; skip.Checked = true;
            append.CheckedChanged += delegate { skip.Enabled = append.Checked; }; layout.Controls.Add(skip, 0, 2);
            list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.HorizontalScrollbar = true; list.AccessibleName = "待导入的话术";
            content.Multiline = true; content.ReadOnly = true; content.ScrollBars = ScrollBars.Vertical; content.Dock = DockStyle.Fill; content.BackColor = Color.White;
            list.SelectedIndexChanged += delegate
            {
                if (list.SelectedIndex < 0) return;
                Phrase phrase = incoming.Library.Phrases[list.SelectedIndex];
                ImportWarning warning = incoming.Warnings.FirstOrDefault(w => w.PhraseId == phrase.Id);
                content.Text = (warning == null ? "" : warning.Description + "\r\n\r\n待导入正文：\r\n") + phrase.Text + (phrase.Attachments.Count == 0 ? "" : "\r\n\r\n附件：\r\n" + String.Join("\r\n", phrase.Attachments.Select(a => a.Name)))
                    + (warning == null ? "" : "\r\n\r\n原始正文（供核对）：\r\n" + warning.OriginalText);
            };
            layout.Controls.Add(list, 0, 3); layout.Controls.Add(content, 0, 4);
            if (incoming.Warnings.Count > 0)
            {
                TableLayoutPanel warningPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
                warningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); warningPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); warningPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                warningPanel.Controls.Add(new Label { Text = "有 " + incoming.Warnings.Count + " 条附件提醒：可用文字继续导入，缺失图片/文件及其标记忽略；仅含缺失附件的记录跳过。导入后保存含原文的提醒报告。", Dock = DockStyle.Fill, ForeColor = Color.DarkRed, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                Button details = new Button { Text = "查看缺图详情", Dock = DockStyle.Fill, Margin = new Padding(3, 12, 0, 12) };
                details.Click += delegate { ShowWarningDetails(); };
                warningPanel.Controls.Add(details, 1, 0); layout.Controls.Add(warningPanel, 0, 5);
            }
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(new Label { Text = "导入前自动备份，保留窗口和发送设置。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            Button confirm = new Button { Text = "确认导入", Dock = DockStyle.Fill };
            Button cancel = new Button { Text = "取消", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
            confirm.Click += delegate
            {
                try
                {
                    UseWaitCursor = true; confirm.Enabled = false;
                    if (targetBoard.SelectedIndex < 0) throw new InvalidOperationException("请先选择导入的大板块。");
                    Result = LibraryTransfer.Import(store, incoming, replace.Checked, skip.Checked, PreserveOriginalBoards ? null : TargetBoardId);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "导入未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                finally { if (!IsDisposed) { UseWaitCursor = false; confirm.Enabled = true; } }
            };
            footer.Controls.Add(confirm, 1, 0); footer.Controls.Add(cancel, 2, 0); layout.Controls.Add(footer, 0, 6); CancelButton = cancel;
            targetBoard.SelectedValueChanged += delegate { RefreshDestination(); };
            Shown += delegate
            {
                targetBoard.SelectedValue = store.Data.Boards.Any(b => b.Id == selectedBoard) ? selectedBoard : Board.PersonalId;
                RefreshDestination(); DialogSizing.FitWorkingArea(this); if (list.Items.Count > 0) list.SelectedIndex = 0;
            };
        }

        private void ShowWarningDetails()
        {
            using (Form details = new Form { Text = "导入提醒 · 原文与缺失附件", Font = Font, Icon = Program.ApplicationIcon,
                Size = new Size(700, 550), MinimumSize = new Size(480, 360), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false })
            {
                TextBox report = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BackColor = Color.White, Text = incoming.WarningReport(), AccessibleName = "导入提醒详情" };
                Button close = new Button { Text = "关闭", Dock = DockStyle.Bottom, Height = 38, DialogResult = DialogResult.Cancel };
                details.Controls.Add(report); details.Controls.Add(close); details.CancelButton = close;
                details.Shown += delegate { DialogSizing.FitWorkingArea(details); report.Select(0, 0); };
                details.ShowDialog(this);
            }
        }

        private void RefreshDestination()
        {
            if (targetBoard.SelectedIndex < 0) return;
            bool preserve = PreserveOriginalBoards; string destination = targetBoard.Text;
            replace.Text = preserve ? "替换全部：按文件内容替换所有大板块及话术" : "替换目标大板块：仅替换所选大板块，保留其他大板块";
            targetHint.Text = preserve ? "按文件中的大板块及分类结构导入。" : "文件中的话术归入「" + destination + "」，同名分类会合并。";
            int selected = list.SelectedIndex; list.BeginUpdate(); list.Items.Clear();
            foreach (Phrase phrase in incoming.Library.Phrases)
            {
                Category group = incoming.Library.Categories.First(c => c.Id == phrase.CategoryId);
                string boardName = preserve ? incoming.Library.Boards.First(b => b.Id == group.BoardId).Name : destination;
                string preview = LocalStore.Preview(phrase); if (preview.Length > 150) preview = preview.Substring(0, 150) + "…";
                list.Items.Add((incoming.Warnings.Any(w => w.PhraseId == phrase.Id) ? "[缺图提醒] " : "") + boardName + " / " + group.Name + " · " + (phrase.Keyword.Length == 0 ? "" : phrase.Keyword + " · ") + preview);
            }
            if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(selected, list.Items.Count - 1)); list.EndUpdate();
        }
    }
}
