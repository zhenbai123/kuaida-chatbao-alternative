using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace QuickReplyLite
{
    internal sealed class WindowsWindowDockingHost : IWindowDockingHost
    {
        private readonly Form follower;
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { internal int Left, Top, Right, Bottom; internal Rectangle Bounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT bounds);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetFrame(IntPtr window, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

        internal WindowsWindowDockingHost(Form form) { follower = form; }
        internal static IntPtr RootWindow(IntPtr window) { return GetAncestor(window, 2); }
        internal static Rectangle VisibleBounds(IntPtr window)
        {
            RECT rectangle;
            if (GetFrame(window, 9, out rectangle, Marshal.SizeOf(typeof(RECT))) == 0 && rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top) return rectangle.Bounds;
            return GetWindowRect(window, out rectangle) ? rectangle.Bounds : Rectangle.Empty;
        }

        public bool TryGetTarget(IntPtr window, out DockingTarget target)
        {
            target = null;
            if (window == IntPtr.Zero || !Native.IsWindow(window) || Native.IsOwnWindow(window) || window == GetShellWindow() || window == GetDesktopWindow()) return false;
            StringBuilder name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            string kind = name.ToString();
            if (kind == "WorkerW" || kind == "Progman" || kind == "Shell_TrayWnd" || kind == "Shell_SecondaryTrayWnd") return false;
            int cloaked; bool hidden = GetCloaked(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            Rectangle bounds = VisibleBounds(window), area = Screen.FromHandle(window).WorkingArea;
            target = new DockingTarget { ProcessId = Native.ProcessId(window), Visible = IsWindowVisible(window) && !hidden, Minimized = IsIconic(window), Bounds = bounds, WorkingArea = area };
            return Native.IsWindow(window);
        }

        public Rectangle FollowerBounds { get { return VisibleBounds(follower.Handle); } }
        public void PlaceFollower(Rectangle bounds)
        {
            Rectangle visible = FollowerBounds, outer = follower.Bounds;
            int left = visible.Left - outer.Left, top = visible.Top - outer.Top;
            int right = outer.Right - visible.Right, bottom = outer.Bottom - visible.Bottom;
            const uint noActivateOrZOrder = 0x0010 | 0x0004 | 0x0200;
            if (!SetWindowPos(follower.Handle, IntPtr.Zero, bounds.Left - left, bounds.Top - top, bounds.Width + left + right, bounds.Height + top + bottom, noActivateOrZOrder))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法跟随目标窗口，请取消吸附后重试。");
        }
        public void SetFollowerVisible(bool visible)
        {
            if (!visible) { follower.Hide(); return; }
            // Showing an existing WinForms handle can restore a child's focus even
            // with ShowWithoutActivation. Temporarily prohibit activation at the
            // native window level; restore the style immediately for normal editing.
            IntPtr window = follower.Handle;
            IntPtr original = GetWindowLongPtr(window, -20);
            SetWindowLongPtr(window, -20, new IntPtr(original.ToInt64() | 0x08000000L));
            try { follower.Show(); }
            finally { SetWindowLongPtr(window, -20, original); }
        }
    }
}
