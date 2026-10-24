using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QuickReplyLite
{
    // Windows integration checks use disposable windows in separate processes.
    // No real chat application, clipboard, startup entry, shortcut or user data is used.
    internal static class WindowDockingSmoke
    {
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        private static int checks;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--target") { Application.Run(ChatWindow()); return 0; }
            string temporary = Path.Combine(Path.GetTempPath(), "kuaida-docking-" + Guid.NewGuid().ToString("N"));
            Process target = null, other = null;
            try
            {
                target = LaunchTarget(); IntPtr handle = WaitForWindow(target);
                LocalStore store = new LocalStore(Path.Combine(temporary, "Data"));
                using (MainForm main = new MainForm(store))
                {
                    main.Show(); Pump();
                    WindowDockingSession session = Field<WindowDockingSession>(main, "docking");
                    CheckBox checkbox = Field<CheckBox>(main, "dockWindow");
                    Check(!checkbox.Checked && !session.IsAttached, "new startup does not inherit an old process window handle");
                    SetField(main, "lastTarget", handle); checkbox.Checked = true;
                    Wait(delegate { return session.IsAttached && IsPlaced(main, handle); });
                    Check(checkbox.Checked && session.Target == handle, "checkbox attaches the actual external window and positions beside it");
                    Check(WindowsWindowDockingHost.VisibleBounds(main.Handle).Left == WindowsWindowDockingHost.VisibleBounds(handle).Right, "the Windows follower touches the target's right visible edge with zero gap");
                    Rectangle initialTarget = WindowsWindowDockingHost.VisibleBounds(handle), initialFollower = WindowsWindowDockingHost.VisibleBounds(main.Handle);
                    Check(initialFollower.Top == initialTarget.Top && initialFollower.Bottom == initialTarget.Bottom, "docked Windows frames have matching top and bottom edges");

                    Rectangle area = Screen.FromHandle(handle).WorkingArea;
                    SetForegroundWindow(handle); Wait(delegate { return Native.GetForegroundWindow() == handle; });
                    Move(handle, area.Left + 70, area.Top + 55, 500, 550);
                    Wait(delegate { return IsPlaced(main, handle); });
                    Check(Native.GetForegroundWindow() == handle, "following a moved target does not steal keyboard focus");
                    Move(handle, area.Left + 70, area.Top + 55, 500, 300);
                    Wait(delegate { return IsPlaced(main, handle); });
                    Check(WindowsWindowDockingHost.VisibleBounds(main.Handle).Height == WindowsWindowDockingHost.VisibleBounds(handle).Height, "shrinking the target also shrinks the follower to the same visible height");
                    Move(handle, area.Right - 510, area.Top + 65, 500, 540);
                    Wait(delegate { return IsPlaced(main, handle); });
                    Check(WindowsWindowDockingHost.VisibleBounds(main.Handle).Left == WindowsWindowDockingHost.VisibleBounds(handle).Right, "near the screen's right edge the follower remains attached on the right instead of switching sides");
                    Check(WindowsWindowDockingHost.VisibleBounds(main.Handle).Right > area.Right, "an off-screen portion is allowed so the right-hand attachment cannot drift or flip");

                    ShowWindow(handle, 6); Wait(delegate { return !main.Visible && session.HiddenByTarget; });
                    Check(session.IsAttached && checkbox.Checked, "minimizing the real target hides quick replies without losing attachment");
                    ShowWindow(handle, 9); IntPtr beforeRestore = Native.GetForegroundWindow();
                    Wait(delegate { return main.Visible && !session.HiddenByTarget && IsPlaced(main, handle); });
                    Console.WriteLine("Restore foreground before=" + beforeRestore + ", after=" + Native.GetForegroundWindow() + ", target=" + handle + ", follower=" + main.Handle);
                    Check(Native.GetForegroundWindow() == beforeRestore, "restoring quick replies preserves whichever application currently has keyboard focus");
                    ShowWindow(handle, 0); Wait(delegate { return !main.Visible; });
                    ShowWindow(handle, 4); Wait(delegate { return main.Visible && IsPlaced(main, handle); });
                    Check(!session.HiddenByTarget, "hidden external windows and no-activation restoration are followed correctly");

                    other = LaunchTarget(); IntPtr otherHandle = WaitForWindow(other);
                    SetForegroundWindow(otherHandle); Wait(delegate { return Native.GetForegroundWindow() == otherHandle; }); Pump();
                    Check(session.Target == handle && Field<IntPtr>(main, "lastTarget") == handle, "switching to a different application cannot silently redirect the docked send target");
                    other.CloseMainWindow(); Wait(delegate { return other.HasExited; }); other.Dispose(); other = null;

                    ShowWindow(handle, 3); Wait(delegate { return IsPlaced(main, handle); });
                    Check(WindowsWindowDockingHost.VisibleBounds(main.Handle).Left == WindowsWindowDockingHost.VisibleBounds(handle).Right, "maximizing the target retains its right-hand attachment without overlaying the chat");
                    ShowWindow(handle, 9); Move(handle, area.Left + 30, area.Top + 35, 500, 560); Wait(delegate { return IsPlaced(main, handle); });
                    // A genuine on-screen demonstration of the two separate windows.
                    if (args.Length > 0)
                    {
                        Directory.CreateDirectory(args[0]);
                        // Match the demo window's height to the follower so the
                        // screenshot contains only the two windows, not runner logs.
                        Move(handle, area.Left + 30, main.Top, 500, main.Height);
                        Wait(delegate { return IsPlaced(main, handle); });
                        SetForegroundWindow(handle); main.TopMost = true; main.Refresh(); Pump();
                        Rectangle combined = Rectangle.Union(WindowsWindowDockingHost.VisibleBounds(handle), WindowsWindowDockingHost.VisibleBounds(main.Handle));
                        using (Bitmap bitmap = new Bitmap(combined.Width, combined.Height))
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        { graphics.CopyFromScreen(combined.Location, Point.Empty, combined.Size); bitmap.Save(Path.Combine(args[0], "docking.png"), ImageFormat.Png); }
                    }

                    checkbox.Checked = false; Rectangle detached = main.Bounds;
                    Check(main.MinimumSize.Height > 0, "detaching restores the normal floating window's minimum height");
                    Move(handle, area.Left + 100, area.Top + 70, 450, 500); Pump();
                    Check(!session.IsAttached && main.Bounds == detached, "unchecking attachment stops movement and restores free floating");
                    SetField(main, "lastTarget", handle); checkbox.Checked = true; Wait(delegate { return IsPlaced(main, handle); });
                    ShowWindow(handle, 6); Wait(delegate { return !main.Visible; });
                    target.CloseMainWindow(); Wait(delegate { return target.HasExited && main.Visible && !session.IsAttached; });
                    Check(!checkbox.Checked && !session.IsAttached, "closing a minimized target restores the app and clears its stale window binding");
                    main.Close(); Pump();
                }
                using (MainForm fresh = new MainForm(new LocalStore(Path.Combine(temporary, "Data"))))
                {
                    fresh.Show(); Pump();
                    Check(!Field<CheckBox>(fresh, "dockWindow").Checked && !Field<WindowDockingSession>(fresh, "docking").IsAttached, "reopening saved data never reuses an obsolete operating-system handle");
                    fresh.Close();
                }
                Console.WriteLine("All " + checks + " Windows docking integration checks passed."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                foreach (Process child in new[] { other, target }) if (child != null) { try { if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(1500)) child.Kill(); } } finally { child.Dispose(); } }
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
        }

        private static Form ChatWindow()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Form form = new Form { Text = "演示聊天窗口 · 吸附功能测试", Font = new Font("Microsoft YaHei UI", 11), StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(area.Left + 25, area.Top + 35, 500, 560), BackColor = Color.FromArgb(242, 246, 252) };
            Label header = new Label { Text = "演示会话", Dock = DockStyle.Top, Height = 72, Padding = new Padding(22, 20, 0, 0), Font = new Font(form.Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(37, 112, 201) };
            Label description = new Label { Text = "快答吸附在这个窗口旁边。\r\n\r\n移动窗口 → 快答跟随\r\n最小化窗口 → 快答收起\r\n恢复窗口 → 快答重新出现\r\n\r\n这里只使用演示内容，不会发送消息。", Dock = DockStyle.Fill, Padding = new Padding(22, 25, 12, 0) };
            TextBox input = new TextBox { Multiline = true, Dock = DockStyle.Bottom, Height = 100, Text = "这里是演示聊天输入框" };
            form.Activated += delegate { input.Select(input.TextLength, 0); };
            form.Controls.Add(description); form.Controls.Add(input); form.Controls.Add(header); return form;
        }

        private static Process LaunchTarget() { return Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--target") { UseShellExecute = false }); }
        private static IntPtr WaitForWindow(Process process) { IntPtr result = IntPtr.Zero; Wait(delegate { process.Refresh(); result = process.MainWindowHandle; return result != IntPtr.Zero; }); return result; }
        private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
        private static void SetField(object instance, string name, object value) { instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value); }
        private static void Pump() { for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(30); } }
        private static void Wait(Func<bool> condition) { DateTime until = DateTime.UtcNow.AddSeconds(6); do { Application.DoEvents(); if (condition()) return; Thread.Sleep(30); } while (DateTime.UtcNow < until); throw new Exception("Timed out waiting for a Windows docking transition."); }
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); checks++; }
        private static void Move(IntPtr window, int x, int y, int width, int height) { if (!SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0010 | 0x0004)) throw new Exception("Cannot move demo window."); }
        private static bool IsPlaced(MainForm main, IntPtr target)
        {
            DockingTarget snapshot; WindowsWindowDockingHost host = new WindowsWindowDockingHost(main);
            if (!host.TryGetTarget(target, out snapshot) || snapshot.Minimized) return false;
            Rectangle current = host.FollowerBounds;
            return current == DockingLayout.Place(snapshot.Bounds, current.Width);
        }
    }
}
