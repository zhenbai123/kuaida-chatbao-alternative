using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("快答·开源版（免激活）")]
[assembly: System.Reflection.AssemblyDescription("本地话术与图片文件一键发送悬浮窗")]
[assembly: System.Reflection.AssemblyVersion("1.7.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.7.2.0")]

namespace QuickReplyLite
{
    internal static class Program
    {
        internal const string EditionName = "快答 · 开源版（免激活）";
        private static Icon appIcon;
        internal static Icon ApplicationIcon { get { if (appIcon == null) appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); return appIcon; } }
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string folder = ChooseDataFolder();
                string key;
                using (SHA256 hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(folder.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
                bool first;
                using (Mutex mutex = new Mutex(true, "Local\\QuickReplyLite-" + key, out first))
                {
                    if (!first) { MessageBox.Show("快答已经在运行，请从任务栏打开。", EditionName); return; }
                    try { WindowsDesktopShortcuts.CreateForCurrentUser(Application.ExecutablePath); }
                    catch (Exception error) { MessageBox.Show("桌面快捷方式未能创建：\r\n" + error.Message + "\r\n进入主窗口后可点击「桌面」重试。", EditionName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    try { Application.Run(new MainForm(new LocalStore(folder))); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error) { MessageBox.Show(error.Message, "快答无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static string ChooseDataFolder()
        {
            string preferred = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            try
            {
                Directory.CreateDirectory(preferred);
                string probe = Path.Combine(preferred, ".write-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, ""); File.Delete(probe); return preferred;
            }
            catch
            {
                // Do not silently switch to an empty store when existing portable data is inaccessible.
                if (File.Exists(Path.Combine(preferred, "话术.json"))) throw new IOException("无法写入已有 Data 文件夹。请把整个程序文件夹移到有写入权限的位置。");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickReplyLite", "Data");
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly LocalStore store;
        private readonly TextBox search = new TextBox();
        private readonly FlowLayoutPanel categories = new FlowLayoutPanel();
        private readonly FlowLayoutPanel boards = new FlowLayoutPanel();
        private readonly SplitContainer library = new SplitContainer();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private readonly ListBox list = new ListBox();
        private readonly Label status = new Label();
        private readonly CheckBox pin = new CheckBox();
        private readonly CheckBox locked = new CheckBox();
        private readonly CheckBox autoSend = new CheckBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly CheckBox dockWindow = new CheckBox();
        private readonly WindowDockingSession docking;
        private bool updatingDocking;
        private readonly IStartupEntry startupEntry = new WindowsStartupEntry();
        private bool updatingStartup;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly System.Windows.Forms.Timer tracker = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer savePosition = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer dragHold = new System.Windows.Forms.Timer();
        private readonly ContextMenuStrip rowMenu = new ContextMenuStrip();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private string activeCategory = "";
        private string activeBoard = Board.PersonalId;
        private bool updatingNavigation;
        private string popupText = "";
        private IntPtr lastTarget;
        private bool ready;
        private bool sending;
        private bool transferring;
        private int tipIndex = -1;
        private bool tipOnArrow;
        private string dragCandidate;
        private bool dragging;
        private int dragSlot = -1;
        private int lastDragScroll;
        private const string PhraseDragFormat = "QuickReplyLite.Phrase";
        private readonly Color blue = Color.FromArgb(37, 112, 201);
        private int S(int value) { return (int)Math.Round(value * DeviceDpi / 96.0); }

        public MainForm(LocalStore value)
        {
            store = value;
            docking = new WindowDockingSession(new WindowsWindowDockingHost(this));
            Text = Program.EditionName;
            Icon = Program.ApplicationIcon;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            MinimumSize = new Size(320, 380);
            Size = new Size(370, 680);
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            KeyPreview = true;

            layout.Dock = DockStyle.Fill; layout.RowCount = 4; layout.ColumnCount = 1; layout.Padding = new Padding(7, 5, 7, 3);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            Controls.Add(layout);
            boards.Dock = DockStyle.Fill; boards.WrapContents = true; boards.AutoScroll = true; boards.Padding = new Padding(0, 1, 0, 1); boards.AccessibleName = "话术板块";
            layout.Controls.Add(boards, 0, 0);

            Panel find = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2, 3, 2, 4) };
            Label findLabel = new Label { Text = "搜索", Dock = DockStyle.Left, Width = 38, TextAlign = ContentAlignment.MiddleLeft };
            search.Dock = DockStyle.Fill;
            search.AccessibleName = "搜索话术";
            search.TextChanged += delegate { RenderPhrases(null); };
            Button batch = new Button { Text = "批量修改", Dock = DockStyle.Right, Width = 82 };
            batch.Click += delegate { EditBatch(); };
            find.Controls.Add(search); find.Controls.Add(findLabel); find.Controls.Add(batch); layout.Controls.Add(find, 0, 1);

            library.Dock = DockStyle.Fill; library.Orientation = Orientation.Horizontal; library.FixedPanel = FixedPanel.Panel1;
            library.SplitterWidth = 7; library.BackColor = Color.FromArgb(234, 239, 245); library.AccessibleName = "拖动分类区底边调整高度";
            library.Paint += delegate(object sender, PaintEventArgs e)
            {
                Rectangle grip = library.SplitterRectangle; int middle = grip.Left + grip.Width / 2;
                using (Pen pen = new Pen(Color.FromArgb(158, 174, 192), Math.Max(1, S(1))))
                    for (int offset = -S(8); offset <= S(8); offset += Math.Max(1, S(8))) e.Graphics.DrawLine(pen, middle + offset, grip.Top + grip.Height / 2 - 1, middle + offset + S(2), grip.Top + grip.Height / 2 - 1);
            };
            library.Panel1.BackColor = Color.White; library.Panel2.BackColor = Color.White; layout.Controls.Add(library, 0, 2);
            categories.Dock = DockStyle.Fill; categories.AutoScroll = true; categories.WrapContents = true; categories.FlowDirection = FlowDirection.LeftToRight;
            categories.Padding = new Padding(0, 4, 0, 4); categories.AccessibleName = "当前板块的分类，自动换行，上下滚动"; library.Panel1.Controls.Add(categories);
            tooltip.SetToolTip(library, "拖动这条分隔线，可调高或调低分类区");
            TableLayoutPanel phraseArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            phraseArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); phraseArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); phraseArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); phraseArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); library.Panel2.Controls.Add(phraseArea);

            FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 2, 0, 2) };
            Button add = MakeButton("＋ 新建", 76); add.Click += delegate { EditPhrase(null); }; bar.Controls.Add(add);
            pin.Text = "置顶"; pin.AutoSize = true; pin.Margin = new Padding(7, 5, 1, 0);
            locked.Text = "锁定"; locked.AutoSize = true; locked.Margin = new Padding(5, 5, 1, 0);
            autoSend.Text = "回车发送"; autoSend.AutoSize = true; autoSend.Margin = new Padding(5, 5, 0, 0);
            bar.Controls.Add(pin); bar.Controls.Add(locked); bar.Controls.Add(autoSend); phraseArea.Controls.Add(bar, 0, 0);
            tooltip.SetToolTip(pin, "保持悬浮窗在其他窗口上方");
            tooltip.SetToolTip(locked, "锁定窗口位置和大小，取消勾选后可拖动");
            tooltip.SetToolTip(autoSend, "勾选：粘贴后按 Enter，保留导入话术的图文顺序；取消：只粘贴文字和附件，由你发送");
            pin.CheckedChanged += delegate { TopMost = pin.Checked; SavePreferences(); };
            locked.CheckedChanged += delegate { SavePreferences(); };
            autoSend.CheckedChanged += delegate { SavePreferences(); };
            FlowLayoutPanel startupBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(3, 0, 0, 0) };
            startup.Text = "开机启动"; startup.AutoSize = true; startup.Margin = new Padding(2, 2, 0, 0); startup.AccessibleName = "登录Windows后自动运行快答";
            startupBar.Controls.Add(startup); phraseArea.Controls.Add(startupBar, 0, 1);
            dockWindow.Text = "吸附当前窗口"; dockWindow.AutoSize = true; dockWindow.Margin = new Padding(14, 2, 0, 0);
            dockWindow.AccessibleName = "吸附最近点击的聊天窗口，跟随移动和最小化";
            startupBar.Controls.Add(dockWindow);
            tooltip.SetToolTip(dockWindow, "先点击聊天窗口，再勾选：快答无缝固定在它的右侧，上下对齐并同高，随它移动和收起。右侧放不下时请将聊天窗口往左移。取消勾选可自由拖动；重启后需重新选择。");
            dockWindow.CheckedChanged += delegate { ChangeDocking(); };
            tooltip.SetToolTip(startup, "勾选后，登录Windows自动运行当前目录的快答；取消勾选即关闭当前程序的开机启动。设置仅属于这台电脑的当前用户。");
            startup.CheckedChanged += delegate { ChangeStartup(); };

            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.FixedSingle; list.DrawMode = DrawMode.OwnerDrawFixed;
            list.IntegralHeight = false; list.ItemHeight = 34; list.AccessibleName = "话术列表，点击左侧箭头发送，双击文字仅粘贴，F2编辑";
            list.DrawItem += DrawPhrase;
            list.MouseDown += ListMouseDown;
            list.MouseUp += delegate { StopDragHold(); };
            list.MouseCaptureChanged += delegate { if (!dragging) StopDragHold(); };
            list.MouseDoubleClick += async delegate(object sender, MouseEventArgs e)
            {
                StopDragHold(); int index = list.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && !dragging && e.X > S(29) && index >= 0 && index < list.Items.Count) await SendPhrase((Phrase)list.Items[index], DeliveryMode.PasteOnly);
            };
            list.AllowDrop = true;
            list.DragEnter += PhraseDragOver; list.DragOver += PhraseDragOver; list.DragDrop += PhraseDragDrop;
            list.DragLeave += delegate { dragSlot = -1; list.Invalidate(); };
            dragHold.Interval = 350; dragHold.Tick += delegate { BeginPhraseDrag(); };
            list.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (dragging) return;
                int index = list.IndexFromPoint(e.Location);
                bool arrow = e.X <= S(29); if (tipIndex == index && tipOnArrow == arrow) return; tipIndex = index; tipOnArrow = arrow;
                if (index < 0 || index >= list.Items.Count) { tooltip.SetToolTip(list, ""); return; }
                Phrase phrase = (Phrase)list.Items[index];
                tooltip.SetToolTip(list, arrow ? "点击箭头发送这条话术\r\n取消「回车发送」则只粘贴" : PreviewText.ForPhrase(phrase));
            };
            list.MouseLeave += delegate { tipIndex = -1; tooltip.Hide(list); };
            phraseArea.Controls.Add(list, 0, 2);
            BuildRowMenu(); list.ContextMenuStrip = rowMenu;
            tooltip.OwnerDraw = true; tooltip.AutoPopDelay = 15000; tooltip.InitialDelay = 450; tooltip.ReshowDelay = 100;
            tooltip.Popup += delegate(object sender, PopupEventArgs e)
            {
                popupText = PreviewText.Wrap(tooltip.GetToolTip(e.AssociatedControl), 25, 16);
                Size measured = TextRenderer.MeasureText(popupText, Font, new Size(Math.Min(S(420), Screen.FromControl(this).WorkingArea.Width - S(30)), 10000), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
                e.ToolTipSize = new Size(measured.Width + S(16), measured.Height + S(12));
            };
            tooltip.Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                e.DrawBackground(); e.DrawBorder();
                TextRenderer.DrawText(e.Graphics, popupText, Font, new Rectangle(S(8), S(6), Math.Max(1, e.Bounds.Width - S(16)), Math.Max(1, e.Bounds.Height - S(12))), SystemColors.InfoText, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
            };

            Panel footer = new Panel { Dock = DockStyle.Fill };
            Button folder = new Button { Text = "数据", Dock = DockStyle.Right, Width = 45, FlatStyle = FlatStyle.Flat, ForeColor = blue };
            folder.FlatAppearance.BorderSize = 0; folder.Click += delegate { OpenDataFolder(); };
            tooltip.SetToolTip(folder, "打开本地数据文件夹，完整复制 Data 即可备份");
            Button import = new Button { Text = "导入", Dock = DockStyle.Right, Width = 45, FlatStyle = FlatStyle.Flat, ForeColor = blue };
            Button export = new Button { Text = "导出", Dock = DockStyle.Right, Width = 45, FlatStyle = FlatStyle.Flat, ForeColor = blue };
            import.FlatAppearance.BorderSize = 0; export.FlatAppearance.BorderSize = 0;
            import.Click += async delegate { await ImportLibrary(); };
            export.Click += delegate
            {
                if (sending || transferring) return;
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("导出全部话术（含图片/文件）", null, async delegate { await ExportLibrary("", false); });
                ToolStripItem boardExport = menu.Items.Add("导出当前板块（含图片/文件）", null, async delegate { await ExportLibrary("", false, activeBoard); }); boardExport.Enabled = store.Data.Categories.Any(c => c.BoardId == activeBoard);
                ToolStripItem current = menu.Items.Add("导出当前分类（含图片/文件）", null, async delegate { await ExportLibrary(activeCategory, false); }); current.Enabled = activeCategory.Length > 0;
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("导出全部文字表格（CSV，不含附件）", null, async delegate { await ExportLibrary("", true); });
                menu.Closed += delegate { if (!IsDisposed) BeginInvoke(new Action(menu.Dispose)); else menu.Dispose(); }; menu.Show(export, new Point(0, export.Height));
            };
            tooltip.SetToolTip(import, "导入快答、聊天宝话术包或UTF-8 CSV，可追加或替换"); tooltip.SetToolTip(export, "将话术连同图片和文件一起打包分享");
            Button desktopShortcut = new Button { Text = "桌面", Dock = DockStyle.Right, Width = 45, FlatStyle = FlatStyle.Flat, ForeColor = blue };
            desktopShortcut.FlatAppearance.BorderSize = 0;
            tooltip.SetToolTip(desktopShortcut, "在桌面创建快答快捷方式。请先完整解压到固定目录，快捷方式指向这份程序。");
            desktopShortcut.Click += delegate
            {
                try { WindowsDesktopShortcuts.CreateForCurrentUser(Application.ExecutablePath); status.Text = "桌面快捷方式已创建。"; }
                catch (Exception error) { ShowError(error); }
            };
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = Color.DimGray;
            status.AutoEllipsis = true; status.Font = new Font(Font.FontFamily, 8F);
            footer.Controls.Add(status); footer.Controls.Add(folder); footer.Controls.Add(export); footer.Controls.Add(import); footer.Controls.Add(desktopShortcut); layout.Controls.Add(footer, 0, 3);

            Load += delegate
            {
                Preferences p = store.Data.Preferences;
                Size = new Size(Math.Max(S(320), Math.Min(S(1000), S(p.Width))), Math.Max(S(380), Math.Min(S(1600), S(p.Height))));
                Rectangle wanted = new Rectangle(p.X, p.Y, Width, Height);
                bool visible = p.X != -1 && Screen.AllScreens.Any(screen => Rectangle.Intersect(screen.WorkingArea, wanted).Width >= S(80) && Rectangle.Intersect(screen.WorkingArea, wanted).Height >= S(60));
                Location = visible ? new Point(p.X, p.Y) : new Point(Math.Max(0, Screen.PrimaryScreen.WorkingArea.Right - Width - S(18)), Screen.PrimaryScreen.WorkingArea.Top + S(60));
                pin.Checked = p.TopMost; locked.Checked = p.Locked; autoSend.Checked = p.AutoSend;
                TopMost = p.TopMost; list.ItemHeight = Math.Max(S(32), Font.Height + S(14));
                activeBoard = store.Data.Boards.Any(b => b.Id == p.ActiveBoardId) ? p.ActiveBoardId : Board.PersonalId;
                library.Panel1MinSize = S(44); library.Panel2MinSize = S(100);
                ready = true; RenderBoards(); RenderCategories(); ClampCategoryHeight(S(p.CategoryPanelHeight > 0 ? p.CategoryPanelHeight : 145)); RenderPhrases(null);
                RefreshStartup();
                if (store.RecoveredBackup) MessageBox.Show(this, "已从上一次自动备份恢复。损坏的原文件已另存到 Data 文件夹。", "恢复提示");
            };
            tracker.Interval = 120; tracker.Tick += delegate
            {
                IntPtr current = Native.GetForegroundWindow();
                bool attachedAtStart = docking.IsAttached;
                if (!sending && !attachedAtStart && current != IntPtr.Zero && Native.IsWindow(current) && !Native.IsOwnWindow(current)) lastTarget = current;
                UpdateDocking();
            }; tracker.Start();
            savePosition.Interval = 500; savePosition.Tick += delegate { savePosition.Stop(); SavePreferences(); };
            Move += delegate { SchedulePositionSave(); }; ResizeEnd += delegate { SchedulePositionSave(); };
            Resize += delegate { if (ready) { UpdateNavigationWidths(); ClampCategoryHeight(library.SplitterDistance); } };
            library.SizeChanged += delegate { if (ready) ClampCategoryHeight(library.SplitterDistance); };
            categories.SizeChanged += delegate { if (ready) UpdateNavigationWidths(); };
            library.SplitterMoved += delegate { SchedulePositionSave(); };
            FormClosing += delegate { StopDragHold(); lifetime.Cancel(); tracker.Stop(); savePosition.Stop(); SavePreferences(); };
            FormClosed += delegate { tracker.Dispose(); savePosition.Dispose(); dragHold.Dispose(); tooltip.Dispose(); lifetime.Dispose(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (transferring) { e.SuppressKeyPress = true; return; }
                if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
                if (e.Control && e.KeyCode == Keys.N) { EditPhrase(null); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.F2 && SelectedPhrase != null) { EditPhrase(SelectedPhrase); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { search.Clear(); e.SuppressKeyPress = true; }
            };
        }

        protected override void WndProc(ref Message message)
        {
            bool movingLocked = (locked != null && locked.Checked) || (docking != null && docking.IsAttached);
            if (message.Msg == 0x0112 && ((movingLocked && (message.WParam.ToInt64() & 0xFFF0) == 0xF010) || (locked != null && locked.Checked && (message.WParam.ToInt64() & 0xFFF0) == 0xF000))) return;
            if (message.Msg == 0x0021 && list != null && list.IsHandleCreated)
            {
                Point point = list.PointToClient(Cursor.Position);
                if (list.ClientRectangle.Contains(point) && point.X <= S(29) && list.IndexFromPoint(point) >= 0)
                { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE preserves the chat's focus for arrow clicks.
            }
            base.WndProc(ref message);
            if (message.Msg == 0x0084)
            {
                long hit = message.Result.ToInt64();
                if ((movingLocked && hit == 2) || (locked != null && locked.Checked && hit >= 10 && hit <= 17)) message.Result = new IntPtr(1);
            }
        }

        protected override bool ShowWithoutActivation
        { get { return (docking != null && docking.HiddenByTarget) || base.ShowWithoutActivation; } }

        private void ChangeDocking()
        {
            if (updatingDocking || !ready) return;
            if (!dockWindow.Checked)
            { docking.Detach(); UpdateDockingSizeLimits(); status.Text = "已取消吸附，可自由拖动快答。"; return; }
            if (sending || transferring || !docking.Attach(WindowsWindowDockingHost.RootWindow(lastTarget)))
            {
                SetDockingChecked(false);
                status.Text = "请先点击要吸附的聊天窗口，再勾选吸附。"; tooltip.SetToolTip(status, status.Text); return;
            }
            lastTarget = docking.Target;
            UpdateDockingSizeLimits();
            status.Text = "已吸附 · 跟随该窗口，取消勾选可解除。"; UpdateDocking();
        }

        private void SetDockingChecked(bool value)
        { updatingDocking = true; try { dockWindow.Checked = value; } finally { updatingDocking = false; UpdateDockingSizeLimits(); } }

        private void UpdateDockingSizeLimits()
        { MinimumSize = new Size(S(320), docking.IsAttached ? 0 : S(380)); }

        private void UpdateDocking()
        {
            if (!docking.IsAttached || sending || transferring || !Enabled || WindowState != FormWindowState.Normal || Application.OpenForms.Count > 1) return;
            try
            {
                if (docking.Update() == DockingUpdate.Detached)
                { SetDockingChecked(false); lastTarget = IntPtr.Zero; status.Text = "目标窗口已关闭，已解除吸附。"; }
            }
            catch (Exception error)
            { docking.Detach(); SetDockingChecked(false); lastTarget = IntPtr.Zero; status.Text = "吸附已停止：" + error.Message; tooltip.SetToolTip(status, status.Text); }
        }

        private Button MakeButton(string text, int width)
        {
            Button button = new Button { Text = text, Width = width, Height = 27, FlatStyle = FlatStyle.Flat, Margin = new Padding(1, 0, 3, 0), BackColor = Color.White, UseMnemonic = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(198, 209, 222); return button;
        }

        private Phrase SelectedPhrase { get { return list.SelectedItem as Phrase; } }

        private void ClampCategoryHeight(int wanted)
        {
            int maximum = library.ClientSize.Height - library.SplitterWidth - library.Panel2MinSize;
            if (maximum >= library.Panel1MinSize) library.SplitterDistance = Math.Max(library.Panel1MinSize, Math.Min(maximum, wanted));
        }

        private void UpdateNavigationWidths()
        {
            if (updatingNavigation) return; updatingNavigation = true;
            try
            {
                foreach (FlowLayoutPanel panel in new[] { boards, categories })
                {
                    int available = Math.Max(S(45), panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - S(8));
                    foreach (Button button in panel.Controls.OfType<Button>()) button.Width = Math.Min(available, Math.Max(S(button.Text == "＋" ? 31 : 48), TextRenderer.MeasureText(button.Text, Font).Width + S(20)));
                    panel.HorizontalScroll.Enabled = false; panel.HorizontalScroll.Visible = false;
                }
                int x = 0, rows = 1, width = Math.Max(1, boards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - S(8));
                foreach (Control control in boards.Controls)
                {
                    int needed = control.Width + control.Margin.Horizontal;
                    if (x > 0 && x + needed > width) { rows++; x = 0; } x += needed;
                }
                float height = S(Math.Min(2, rows) * 34 + 4);
                if (layout.RowStyles[0].Height != height) layout.RowStyles[0].Height = height;
            }
            finally { updatingNavigation = false; }
        }

        private void RenderBoards()
        {
            if (!store.Data.Boards.Any(b => b.Id == activeBoard)) { activeBoard = Board.PersonalId; activeCategory = ""; }
            boards.SuspendLayout();
            foreach (Control control in boards.Controls.Cast<Control>().ToArray()) control.Dispose(); boards.Controls.Clear();
            foreach (Board board in store.Data.Boards)
            {
                Button button = MakeButton(board.Name, 60); button.Tag = board.Id; button.Height = S(30); button.AutoEllipsis = true; button.Margin = new Padding(S(2), S(2), S(4), S(2));
                if (board.Id == activeBoard) { button.ForeColor = blue; button.BackColor = Color.FromArgb(232, 242, 255); button.FlatAppearance.BorderColor = blue; }
                button.Click += delegate
                {
                    if (sending || transferring) return; activeBoard = board.Id; activeCategory = ""; search.Clear(); RenderBoards(); RenderCategories(); RenderPhrases(null); SavePreferences();
                };
                if (board.Id != Board.PublicId && board.Id != Board.PersonalId)
                {
                    ContextMenuStrip menu = new ContextMenuStrip();
                    menu.Items.Add("重命名板块", null, delegate
                    {
                        string name = AskText("重命名板块", board.Name); if (name == null) return;
                        try { store.RenameBoard(board.Id, name); RenderBoards(); } catch (Exception error) { ShowError(error); }
                    });
                    menu.Items.Add("删除板块（话术移入个人）", null, delegate
                    {
                        if (MessageBox.Show(this, "删除此板块？分类和话术会移入个人，同名分类合并。", "删除板块", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                        try { store.DeleteBoard(board.Id); activeBoard = Board.PersonalId; activeCategory = ""; RenderBoards(); RenderCategories(); RenderPhrases(null); } catch (Exception error) { ShowError(error); }
                    });
                    button.ContextMenuStrip = menu; button.Disposed += delegate { menu.Dispose(); };
                }
                boards.Controls.Add(button); tooltip.SetToolTip(button, board.Name + "\r\n板块中的分类和话术单独显示");
            }
            Button plus = MakeButton("＋", S(31)); plus.Height = S(30); plus.Margin = new Padding(S(2), S(2), S(4), S(2));
            plus.Click += delegate
            {
                string name = AskText("添加板块", ""); if (name == null) return;
                try { activeBoard = store.AddBoard(name).Id; activeCategory = ""; search.Clear(); RenderBoards(); RenderCategories(); RenderPhrases(null); SavePreferences(); } catch (Exception error) { ShowError(error); }
            };
            boards.Controls.Add(plus); tooltip.SetToolTip(plus, "添加自定义板块；右键自定义板块可重命名、删除"); boards.ResumeLayout(); UpdateNavigationWidths();
            Button current = boards.Controls.OfType<Button>().FirstOrDefault(b => (string)b.Tag == activeBoard); if (current != null) boards.ScrollControlIntoView(current);
        }

        private void RenderCategories()
        {
            categories.SuspendLayout();
            foreach (Control control in categories.Controls.Cast<Control>().ToArray()) control.Dispose();
            categories.Controls.Clear();
            AddCategoryButton("全部", "");
            foreach (Category item in store.Data.Categories.Where(c => c.BoardId == activeBoard)) AddCategoryButton(item.Name, item.Id);
            Button plus = MakeButton("＋", S(31)); plus.Height = S(30); plus.Margin = new Padding(S(2), S(2), S(4), S(2)); plus.Click += delegate { AddCategory(); }; categories.Controls.Add(plus);
            tooltip.SetToolTip(plus, "新建分类；右键分类可重命名或删除");
            categories.ResumeLayout(); UpdateNavigationWidths();
            Button current = categories.Controls.OfType<Button>().FirstOrDefault(b => (string)b.Tag == activeCategory); if (current != null) categories.ScrollControlIntoView(current);
        }

        private void AddCategoryButton(string name, string id)
        {
            Button button = MakeButton(name, Math.Max(S(48), TextRenderer.MeasureText(name, Font).Width + S(18)));
            button.Tag = id;
            button.Height = S(30); button.Margin = new Padding(S(2), S(2), S(4), S(2)); button.AutoEllipsis = true;
            if (id == activeCategory) { button.BackColor = blue; button.ForeColor = Color.White; }
            button.Click += delegate { activeCategory = id; RenderCategories(); RenderPhrases(null); };
            if (id.Length > 0)
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("重命名分类", null, delegate { RenameCategory(id); });
                menu.Items.Add("删除分类（保留话术）", null, delegate { DeleteCategory(id); });
                ToolStripMenuItem move = new ToolStripMenuItem("移动到板块");
                foreach (Board board in store.Data.Boards.Where(b => b.Id != activeBoard)) move.DropDownItems.Add(board.Name, null, delegate
                {
                    try { store.MoveCategoryToBoard(id, board.Id); activeCategory = ""; RenderCategories(); RenderPhrases(null); } catch (Exception error) { ShowError(error); }
                });
                menu.Items.Add(move);
                button.ContextMenuStrip = menu; button.Disposed += delegate { menu.Dispose(); };
            }
            categories.Controls.Add(button);
            tooltip.SetToolTip(button, name + (id.Length > 0 ? "\r\n右键管理分类或移动到其他板块" : "\r\n显示当前板块的全部话术"));
        }

        private void RenderPhrases(string selectedId)
        {
            StopDragHold(); tipIndex = -1; dragSlot = -1; tooltip.Hide(list); tooltip.SetToolTip(list, "");
            if (selectedId == null && SelectedPhrase != null) selectedId = SelectedPhrase.Id;
            List<Phrase> phrases = store.Search(activeCategory, search.Text, activeBoard);
            list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(phrases.Cast<object>().ToArray());
            int selected = phrases.FindIndex(p => p.Id == selectedId);
            if (selected >= 0) list.SelectedIndex = selected;
            list.EndUpdate();
            status.Text = phrases.Count + " 条 · 双击仅粘贴 / →发送";
            tooltip.SetToolTip(status, "数据保存在本机：" + store.Folder);
        }

        private void DrawPhrase(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            Phrase phrase = (Phrase)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? Color.FromArgb(232, 242, 255) : e.Index % 2 == 0 ? Color.White : Color.FromArgb(249, 250, 252);
            using (Brush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            int middle = e.Bounds.Top + e.Bounds.Height / 2;
            using (Pen pen = new Pen(blue, Math.Max(1.5F, S(2))))
            {
                e.Graphics.DrawLine(pen, S(8), middle, S(22), middle);
                e.Graphics.DrawLine(pen, S(16), middle - S(5), S(22), middle);
                e.Graphics.DrawLine(pen, S(16), middle + S(5), S(22), middle);
            }
            TextRenderer.DrawText(e.Graphics, (e.Index + 1).ToString("00") + ".", Font, new Rectangle(S(32), e.Bounds.Top, S(29), e.Bounds.Height), Color.Gray, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int x = S(64);
            if (!String.IsNullOrWhiteSpace(phrase.Keyword))
            {
                int badgeWidth = Math.Min(S(110), TextRenderer.MeasureText(phrase.Keyword, Font).Width + S(10));
                Rectangle badge = new Rectangle(x, middle - (Font.Height + S(4)) / 2, badgeWidth, Font.Height + S(4));
                Color color = Color.Transparent;
                try { if (!String.IsNullOrWhiteSpace(phrase.ColorHex)) color = ColorTranslator.FromHtml(phrase.ColorHex); } catch { }
                if (color != Color.Transparent) using (Brush brush = new SolidBrush(color)) e.Graphics.FillRectangle(brush, badge);
                Color foreground = color == Color.Transparent ? blue : color.GetBrightness() > 0.7F ? Color.FromArgb(35, 35, 35) : Color.White;
                TextRenderer.DrawText(e.Graphics, phrase.Keyword, Font, badge, foreground, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += badgeWidth + S(6);
            }
            int reserve = phrase.Attachments.Count > 0 ? S(31) : S(5);
            Rectangle textRect = new Rectangle(x, e.Bounds.Top, Math.Max(0, e.Bounds.Width - x - reserve), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, LocalStore.Preview(phrase), Font, textRect, Color.FromArgb(38, 38, 38), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (phrase.Attachments.Count > 0)
                using (Font countFont = new Font(Font.FontFamily, 7.5F))
                    TextRenderer.DrawText(e.Graphics, "件" + phrase.Attachments.Count, countFont, new Rectangle(e.Bounds.Right - S(31), e.Bounds.Top, S(30), e.Bounds.Height), blue, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (Pen pen = new Pen(Color.FromArgb(239, 241, 244))) e.Graphics.DrawLine(pen, 0, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (dragging && (dragSlot == e.Index || (dragSlot == list.Items.Count && e.Index == list.Items.Count - 1)))
            {
                int y = dragSlot == e.Index ? e.Bounds.Top + S(1) : e.Bounds.Bottom - S(2);
                using (Pen marker = new Pen(blue, S(3))) e.Graphics.DrawLine(marker, 0, y, e.Bounds.Right, y);
            }
        }

        private async void ListMouseDown(object sender, MouseEventArgs e)
        {
            StopDragHold();
            int index = list.IndexFromPoint(e.Location);
            if (index < 0 || index >= list.Items.Count) return;
            list.SelectedIndex = index;
            if (e.Button == MouseButtons.Left && e.X <= S(29)) await SendPhrase((Phrase)list.Items[index], autoSend.Checked ? DeliveryMode.Send : DeliveryMode.PasteOnly);
            else if (e.Button == MouseButtons.Left && e.Clicks == 1 && !sending)
            { dragCandidate = ((Phrase)list.Items[index]).Id; dragHold.Start(); }
        }

        private void StopDragHold() { dragHold.Stop(); dragCandidate = null; }

        private void BeginPhraseDrag()
        {
            string id = dragCandidate; dragHold.Stop();
            if (id == null || sending || Control.MouseButtons != MouseButtons.Left || !list.ClientRectangle.Contains(list.PointToClient(Cursor.Position))) { StopDragHold(); return; }
            dragging = true; tooltip.Hide(list); lastDragScroll = Environment.TickCount;
            status.Text = "拖到蓝色插入线的位置松开，Esc 可取消。";
            try { list.DoDragDrop(new DataObject(PhraseDragFormat, id), DragDropEffects.Move); }
            finally { dragging = false; StopDragHold(); dragSlot = -1; list.Invalidate(); }
        }

        private void PhraseDragOver(object sender, DragEventArgs e)
        {
            if (!dragging || sending || !e.Data.GetDataPresent(PhraseDragFormat) || list.Items.Count == 0)
            { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            Point point = list.PointToClient(new Point(e.X, e.Y));
            if (unchecked(Environment.TickCount - lastDragScroll) >= 110)
            {
                if (point.Y < S(22) && list.TopIndex > 0) list.TopIndex--;
                else if (point.Y > list.ClientSize.Height - S(22) && list.TopIndex < list.Items.Count - 1) list.TopIndex++;
                lastDragScroll = Environment.TickCount;
            }
            int index = list.IndexFromPoint(new Point(Math.Max(0, Math.Min(list.ClientSize.Width - 1, point.X)), Math.Max(0, Math.Min(list.ClientSize.Height - 1, point.Y))));
            int slot = index < 0 ? list.Items.Count : index + (point.Y >= list.GetItemRectangle(index).Top + list.ItemHeight / 2 ? 1 : 0);
            if (dragSlot != slot) { dragSlot = slot; list.Invalidate(); }
        }

        private void PhraseDragDrop(object sender, DragEventArgs e)
        {
            if (!dragging || dragSlot < 0 || !e.Data.GetDataPresent(PhraseDragFormat) || list.Items.Count == 0) return;
            string id = e.Data.GetData(PhraseDragFormat) as string;
            bool after = dragSlot >= list.Items.Count;
            Phrase target = (Phrase)list.Items[after ? list.Items.Count - 1 : dragSlot];
            try
            {
                bool moved = store.MovePhrase(id, target.Id, after); RenderPhrases(id);
                if (moved) status.Text = "话术顺序已保存。";
            }
            catch (Exception error) { ShowError(error); }
        }

        private void BuildRowMenu()
        {
            rowMenu.Items.Add("发送话术", null, async delegate { if (SelectedPhrase != null) await SendPhrase(SelectedPhrase, DeliveryMode.Send); });
            rowMenu.Items.Add("仅粘贴（双击，不回车）", null, async delegate { if (SelectedPhrase != null) await SendPhrase(SelectedPhrase, DeliveryMode.PasteOnly); });
            rowMenu.Items.Add("复制文字", null, delegate { if (SelectedPhrase != null) CopyText(SelectedPhrase); });
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add("新建话术", null, delegate { EditPhrase(null); });
            rowMenu.Items.Add("修改话术（F2）", null, delegate { if (SelectedPhrase != null) EditPhrase(SelectedPhrase); });
            rowMenu.Items.Add("删除话术", null, delegate { DeletePhrase(); });
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add("上移", null, delegate { MovePhrase(-1); });
            rowMenu.Items.Add("下移", null, delegate { MovePhrase(1); });
            rowMenu.Opening += delegate { StopDragHold(); bool has = SelectedPhrase != null; foreach (ToolStripItem item in rowMenu.Items) if (!(item is ToolStripSeparator) && item.Text != "新建话术") item.Enabled = has && !sending; };
        }

        private async Task SendPhrase(Phrase phrase, DeliveryMode mode)
        {
            if (sending || IsDisposed) return;
            bool wasAttached = docking.IsAttached; UpdateDocking();
            if (wasAttached && !docking.IsAttached) return;
            IntPtr target = lastTarget;
            bool submit = mode == DeliveryMode.Send;
            string[] files = phrase.Attachments.Select(a => store.Resolve(a)).ToArray();
            try
            {
                foreach (string file in files) if (!File.Exists(file)) throw new FileNotFoundException("附件丢失，请重新添加：" + Path.GetFileName(file));
                sending = true; dockWindow.Enabled = false; UseWaitCursor = true; StopDragHold(); tooltip.Hide(list); status.Text = submit ? "正在发送，请勿切换聊天窗口…" : "正在放入聊天框，请勿切换窗口…";
                foreach (DeliveryStep step in DeliveryPlan.Build(phrase, mode))
                {
                    await Native.FocusTarget(target, lifetime.Token);
                    bool attachment = step.AttachmentIndices.Length > 0;
                    if (attachment) await Native.WriteClipboard(delegate { Native.PutFiles(step.AttachmentIndices.Select(i => files[i]).ToArray()); }, lifetime.Token);
                    else await Native.WriteClipboard(delegate { Clipboard.SetText(step.Text, TextDataFormat.UnicodeText); }, lifetime.Token);
                    Native.Paste(target); await Task.Delay(attachment ? 1000 : 350, lifetime.Token);
                    if (step.PressEnter && !Native.Confirm(target, attachment)) { status.Text = "内容已粘贴，请在聊天窗口确认；后续分段已停止。"; return; }
                    if (step.PressEnter) await Task.Delay(220, lifetime.Token);
                }
                status.Text = submit ? "已执行发送 · 是否成功以聊天窗口为准" : "已粘贴 · 请在聊天窗口确认并发送";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) { status.Text = error.Message; tooltip.SetToolTip(status, error.Message); } }
            finally { sending = false; if (!IsDisposed) { dockWindow.Enabled = true; UseWaitCursor = false; } }
        }

        private void CopyText(Phrase phrase)
        {
            try { string text = PhraseContent.PlainText(phrase); if (String.IsNullOrEmpty(text)) { status.Text = "这条话术只有附件，请使用左侧箭头。"; return; } Clipboard.SetText(text); status.Text = "文字已复制。"; }
            catch { status.Text = "剪贴板被占用，请稍后再试。"; }
        }

        private void EditPhrase(Phrase phrase)
        {
            if (sending) return;
            StopDragHold();
            try
            {
                HashSet<string> before = new HashSet<string>(store.Data.Phrases.Select(p => p.Id));
                string category = activeCategory;
                if (phrase == null && category.Length == 0) category = (store.Data.Categories.FirstOrDefault(c => c.BoardId == activeBoard) ?? store.AddCategory("常用", activeBoard)).Id;
                using (PhraseEditor editor = new PhraseEditor(store, phrase, category)) editor.ShowDialog(this);
                Phrase edited = phrase == null ? store.Data.Phrases.LastOrDefault(p => !before.Contains(p.Id)) : store.Data.Phrases.FirstOrDefault(p => p.Id == phrase.Id);
                if (edited != null) { activeCategory = edited.CategoryId; activeBoard = store.Data.Categories.First(c => c.Id == edited.CategoryId).BoardId; }
                RenderBoards(); RenderCategories(); RenderPhrases(edited == null ? null : edited.Id);
            }
            catch (Exception error) { ShowError(error); }
        }

        private void EditBatch()
        {
            if (sending) return;
            StopDragHold();
            using (BatchReplaceForm editor = new BatchReplaceForm(store))
            {
                editor.ShowDialog(this); RenderPhrases(null);
                if (editor.ChangedCount > 0) status.Text = "已批量修改 " + editor.ChangedCount + " 条话术。";
            }
        }

        private void DeletePhrase()
        {
            Phrase phrase = SelectedPhrase;
            if (phrase == null || sending) return;
            if (MessageBox.Show(this, "删除这条话术？本地附件仍会保留在素材文件夹。", "删除话术", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int index = store.Data.Phrases.IndexOf(phrase); store.Data.Phrases.Remove(phrase);
            try { store.Save(); RenderPhrases(null); }
            catch (Exception error) { store.Data.Phrases.Insert(index, phrase); ShowError(error); }
        }

        private void MovePhrase(int delta)
        {
            int index = list.SelectedIndex;
            if (index < 0 || index + delta < 0 || index + delta >= list.Items.Count || sending) return;
            Phrase first = (Phrase)list.Items[index], other = (Phrase)list.Items[index + delta];
            int a = store.Data.Phrases.IndexOf(first), b = store.Data.Phrases.IndexOf(other);
            store.Data.Phrases[a] = other; store.Data.Phrases[b] = first;
            try { store.Save(); RenderPhrases(first.Id); }
            catch (Exception error) { store.Data.Phrases[a] = first; store.Data.Phrases[b] = other; ShowError(error); }
        }

        private void AddCategory()
        {
            string value = AskText("新建分类", ""); if (value == null) return;
            try { activeCategory = store.AddCategory(value, activeBoard).Id; search.Clear(); RenderCategories(); RenderPhrases(null); }
            catch (Exception error) { ShowError(error); }
        }

        private void RenameCategory(string id)
        {
            Category category = store.Data.Categories.First(c => c.Id == id);
            string value = AskText("重命名分类", category.Name); if (value == null) return;
            value = value.Trim();
            if (value.Length == 0 || value.Length > 30 || store.Data.Categories.Any(c => c.Id != id && c.BoardId == category.BoardId && String.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase)))
            { ShowError(new Exception("请输入不重复的分类名称（最多30字）。")); return; }
            string old = category.Name; category.Name = value;
            try { store.Save(); RenderCategories(); } catch (Exception error) { category.Name = old; ShowError(error); }
        }

        private void DeleteCategory(string id)
        {
            if (store.Data.Categories.Count <= 1) { ShowError(new Exception("至少保留一个分类。")); return; }
            if (MessageBox.Show(this, "删除分类后，里面的话术会移入当前板块的另一个分类，不会删除话术。", "删除分类", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try { store.DeleteCategory(id); activeCategory = ""; RenderCategories(); RenderPhrases(null); } catch (Exception error) { ShowError(error); }
        }

        private string AskText(string title, string value)
        {
            using (Form dialog = new Form { Text = title, Icon = Program.ApplicationIcon, Size = new Size(340, 155), Font = Font, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                TextBox input = new TextBox { Text = value, Left = 16, Top = 18, Width = 290, MaxLength = 30 };
                Button ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Left = 145, Top = 62, Width = 78 };
                Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 229, Top = 62, Width = 78 };
                dialog.Controls.AddRange(new Control[] { input, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.Shown += delegate { input.Focus(); input.SelectAll(); };
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }

        private void OpenDataFolder() { try { Process.Start(new ProcessStartInfo(store.Folder) { UseShellExecute = true }); } catch (Exception error) { ShowError(error); } }

        private void TransferBusy(bool value)
        {
            transferring = value;
            if (!IsDisposed) { UseWaitCursor = value; Controls[0].Enabled = !value; }
        }

        private async Task ImportLibrary()
        {
            if (sending || transferring) return;
            StopDragHold(); string path;
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "导入话术", CheckFileExists = true, Filter = "话术包（快答 / 聊天宝）|*.quickreply;*.bangkefu;*.zip|聊天宝个人知识库|*.bangkefu|UTF-8 CSV文字表格|*.csv|所有文件|*.*" })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName; }
            ImportedLibrary incoming = null;
            try
            {
                TransferBusy(true); status.Text = "正在读取并校验话术包…";
                incoming = await Task.Run(delegate { return LibraryTransfer.Load(path); });
                if (IsDisposed) return;
                TransferBusy(false);
                using (ImportLibraryForm preview = new ImportLibraryForm(store, incoming, activeBoard))
                {
                    if (preview.ShowDialog(this) == DialogResult.OK)
                    {
                        if (!preview.PreserveOriginalBoards) activeBoard = preview.TargetBoardId;
                        else
                        {
                            string boardName = incoming.Library.Boards.First(b => b.Id == incoming.Library.Categories[0].BoardId).Name;
                            activeBoard = store.Data.Boards.First(b => String.Equals(b.Name, boardName, StringComparison.OrdinalIgnoreCase)).Id;
                        }
                        activeCategory = ""; search.Clear(); RenderBoards(); RenderCategories(); RenderPhrases(null);
                        status.Text = "已导入 " + preview.Result.Added + " 条，跳过重复 " + preview.Result.Skipped + " 条。";
                        tooltip.SetToolTip(status, status.Text + (preview.Result.BackupPath == null ? "" : "\n导入前备份：" + preview.Result.BackupPath) + (preview.Result.ReportPath == null ? "" : "\n缺图提醒报告：" + preview.Result.ReportPath));
                        if (preview.Result.ReportPath != null) MessageBox.Show(this, "已导入可用话术和附件。原文件有 " + incoming.Warnings.Count + " 条话术的部分图片标记没有对应附件，请核对并补充缺失内容。\r\n\r\n含原始正文的提醒报告已保存：\r\n" + preview.Result.ReportPath + "\r\n\r\n点击右下角「数据」可打开报告所在文件夹。", "导入完成 · 有缺图提醒", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else RenderPhrases(null);
                }
            }
            catch (Exception error) { if (!IsDisposed) { status.Text = "导入未完成。"; ShowError(error); } }
            finally { if (incoming != null) incoming.Dispose(); TransferBusy(false); }
        }

        private async Task ExportLibrary(string category, bool csv, string boardId = null)
        {
            if (sending || transferring) return;
            StopDragHold(); string path;
            using (SaveFileDialog dialog = new SaveFileDialog { Title = csv ? "导出文字表格（不含图片/文件）" : "导出完整话术包", Filter = csv ? "UTF-8 CSV文字表格|*.csv" : "快答话术包（含图片/文件）|*.quickreply", DefaultExt = csv ? "csv" : "quickreply", AddExtension = true,
                FileName = LibraryTransfer.SafeName(!String.IsNullOrEmpty(boardId) ? store.Data.Boards.First(b => b.Id == boardId).Name : category.Length == 0 ? "全部话术" : store.Data.Categories.First(c => c.Id == category).Name) + (csv ? "-文字表格.csv" : ".quickreply") })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName; }
            try
            {
                TransferBusy(true); status.Text = "正在导出话术…";
                int count = await Task.Run(delegate { if (csv) return LibraryTransfer.ExportCsv(store, path, category); LibraryTransfer.ExportPackage(store, path, category, boardId); HashSet<string> included = new HashSet<string>(store.Data.Categories.Where(c => String.IsNullOrEmpty(boardId) || c.BoardId == boardId).Select(c => c.Id)); return store.Data.Phrases.Count(p => included.Contains(p.CategoryId) && (category.Length == 0 || p.CategoryId == category)); });
                if (!IsDisposed) { status.Text = "已导出 " + count + " 条" + (csv ? "文字话术（附件请用完整包）。" : "话术，包含关联附件。"); tooltip.SetToolTip(status, path); }
            }
            catch (Exception error) { if (!IsDisposed) { status.Text = "导出未完成。"; ShowError(error); } }
            finally { TransferBusy(false); }
        }
        private void SchedulePositionSave() { if (ready && WindowState == FormWindowState.Normal) { savePosition.Stop(); savePosition.Start(); } }
        private void RefreshStartup()
        {
            updatingStartup = true;
            try { startup.Checked = StartupRegistration.IsEnabled(startupEntry, Application.ExecutablePath); }
            catch (Exception error) { startup.Enabled = false; startup.Text = "开机启动（读取失败）"; tooltip.SetToolTip(startup, error.Message); }
            finally { updatingStartup = false; }
        }
        private void ChangeStartup()
        {
            if (!ready || updatingStartup) return;
            bool requested = startup.Checked;
            try
            {
                StartupRegistration.SetEnabled(startupEntry, Application.ExecutablePath, requested);
                status.Text = requested ? "已设置：登录Windows自动运行快答。" : "已取消当前程序的开机启动。";
            }
            catch (Exception error) { RefreshStartup(); ShowError(new InvalidOperationException("开机启动设置未完成：" + error.Message)); }
        }
        private void SavePreferences()
        {
            if (!ready || IsDisposed) return;
            Preferences p = store.Data.Preferences; p.TopMost = pin.Checked; p.Locked = locked.Checked; p.AutoSend = autoSend.Checked;
            p.ActiveBoardId = activeBoard; p.CategoryPanelHeight = (int)Math.Round(library.SplitterDistance * 96.0 / DeviceDpi);
            if (WindowState == FormWindowState.Normal) { p.X = Left; p.Y = Top; p.Width = (int)Math.Round(Width * 96.0 / DeviceDpi); p.Height = (int)Math.Round(Height * 96.0 / DeviceDpi); }
            try { store.Save(); } catch (Exception error) { status.Text = "保存失败：" + error.Message; tooltip.SetToolTip(status, status.Text); }
        }
        private void ShowError(Exception error) { MessageBox.Show(this, error.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

}
