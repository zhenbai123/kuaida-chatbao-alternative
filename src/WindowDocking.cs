using System;
using System.Drawing;

namespace QuickReplyLite
{
    public sealed class DockingTarget
    {
        public uint ProcessId;
        public bool Visible;
        public bool Minimized;
        public Rectangle Bounds;
        public Rectangle WorkingArea;
    }

    public interface IWindowDockingHost
    {
        bool TryGetTarget(IntPtr window, out DockingTarget target);
        Rectangle FollowerBounds { get; }
        void PlaceFollower(Rectangle bounds);
        void SetFollowerVisible(bool visible);
    }

    public enum DockingUpdate { Inactive, Following, Hidden, Detached }

    public static class DockingLayout
    {
        public static Rectangle Place(Rectangle target, Size follower, Rectangle area, int gap)
        {
            if (target.Width <= 0 || target.Height <= 0 || follower.Width <= 0 || follower.Height <= 0 || area.Width <= 0 || area.Height <= 0 || gap < 0)
                throw new ArgumentException("窗口尺寸或屏幕工作区无效。");
            int width = Math.Min(follower.Width, area.Width), height = Math.Min(follower.Height, area.Height);
            long right = (long)target.Right + gap, left = (long)target.Left - gap - width;
            long x = right + width <= area.Right ? right : left >= area.Left ? left : (long)area.Right - width;
            x = Math.Max(area.Left, Math.Min(x, (long)area.Right - width));
            int y = Math.Max(area.Top, Math.Min(target.Top, area.Bottom - height));
            return new Rectangle((int)x, y, width, height);
        }
    }

    // The bound handle is session-only. Never persist or silently rebind it to
    // whatever application happens to become the foreground window later.
    public sealed class WindowDockingSession
    {
        private readonly IWindowDockingHost host;
        private uint processId;
        public IntPtr Target { get; private set; }
        public bool IsAttached { get { return Target != IntPtr.Zero; } }
        public bool HiddenByTarget { get; private set; }

        public WindowDockingSession(IWindowDockingHost value)
        { if (value == null) throw new ArgumentNullException("value"); host = value; }

        public bool Attach(IntPtr window)
        {
            DockingTarget candidate;
            if (window == IntPtr.Zero || !host.TryGetTarget(window, out candidate) || candidate == null || candidate.ProcessId == 0) return false;
            if (candidate.Bounds.Width <= 0 || candidate.Bounds.Height <= 0 || candidate.WorkingArea.Width <= 0 || candidate.WorkingArea.Height <= 0) return false;
            Detach(); Target = window; processId = candidate.ProcessId; return true;
        }

        public DockingUpdate Update(int gap)
        {
            if (!IsAttached) return DockingUpdate.Inactive;
            DockingTarget target;
            if (!host.TryGetTarget(Target, out target) || target == null || target.ProcessId != processId)
            { Detach(); return DockingUpdate.Detached; }
            if (!target.Visible || target.Minimized)
            {
                if (!HiddenByTarget) { host.SetFollowerVisible(false); HiddenByTarget = true; }
                return DockingUpdate.Hidden;
            }
            RestoreVisibility();
            Rectangle bounds = DockingLayout.Place(target.Bounds, host.FollowerBounds.Size, target.WorkingArea, gap);
            if (host.FollowerBounds != bounds) host.PlaceFollower(bounds);
            return DockingUpdate.Following;
        }

        public void Detach()
        { Target = IntPtr.Zero; processId = 0; RestoreVisibility(); }

        private void RestoreVisibility()
        {
            if (!HiddenByTarget) return;
            // MainForm.ShowWithoutActivation reads this flag during Show().
            host.SetFollowerVisible(true); HiddenByTarget = false;
        }
    }
}
