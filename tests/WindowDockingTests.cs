using System;
using System.Drawing;
using QuickReplyLite;

internal static class WindowDockingTests
{
    private sealed class Host : IWindowDockingHost
    {
        internal DockingTarget Target = new DockingTarget { ProcessId = 42, Visible = true, Bounds = new Rectangle(100, 120, 600, 500), WorkingArea = new Rectangle(0, 0, 1600, 900) };
        internal bool Exists = true;
        internal Rectangle Bounds = new Rectangle(50, 50, 370, 680);
        internal int Moves, Shows, Hides;
        public bool TryGetTarget(IntPtr window, out DockingTarget target) { target = Target; return Exists && window == new IntPtr(7); }
        public Rectangle FollowerBounds { get { return Bounds; } }
        public void PlaceFollower(Rectangle value) { Bounds = value; Moves++; }
        public void SetFollowerVisible(bool visible) { if (visible) Shows++; else Hides++; }
    }

    internal static void Run(Action<bool, string> check)
    {
        Rectangle area = new Rectangle(0, 0, 1920, 1040);
        Rectangle rightTarget = new Rectangle(100, 100, 700, 600);
        Rectangle flush = DockingLayout.Place(rightTarget, new Size(370, 680), area, 0);
        check(flush.Left == rightTarget.Right && flush.Top == rightTarget.Top, "zero-gap docking touches the target's right edge without a spacer");
        Rectangle leftTarget = new Rectangle(1200, 180, 700, 600);
        flush = DockingLayout.Place(leftTarget, new Size(370, 680), area, 0);
        check(flush.Right == leftTarget.Left && flush.Top == leftTarget.Top, "zero-gap docking also touches the left edge when the right side has insufficient space");
        Rectangle negativeTarget = new Rectangle(-1700, -180, 900, 700);
        flush = DockingLayout.Place(negativeTarget, new Size(370, 680), new Rectangle(-1920, -200, 1920, 1040), 0);
        check(flush.Left == negativeTarget.Right, "zero-gap docking remains flush on monitors with negative coordinates");
        Rectangle result = DockingLayout.Place(new Rectangle(100, 100, 700, 600), new Size(370, 680), area, 4);
        check(result == new Rectangle(804, 100, 370, 680), "docking aligns the follower to the target's right edge with a small gap");
        result = DockingLayout.Place(new Rectangle(1200, 180, 700, 600), new Size(370, 680), area, 4);
        check(result == new Rectangle(826, 180, 370, 680), "docking uses the left side when the right side has insufficient room");
        result = DockingLayout.Place(area, new Size(370, 680), area, 4);
        check(result == new Rectangle(1550, 0, 370, 680), "a maximized target keeps the follower visible inside the monitor edge");
        result = DockingLayout.Place(new Rectangle(-1700, -180, 900, 700), new Size(370, 680), new Rectangle(-1920, -200, 1920, 1040), 4);
        check(result == new Rectangle(-796, -180, 370, 680), "docking supports monitors to the left and above the primary display");
        result = DockingLayout.Place(new Rectangle(20, 900, 900, 100), new Size(370, 680), area, 4);
        check(result.Top == 360 && result.Bottom == area.Bottom, "docking clamps vertically to the working area and avoids the taskbar");
        result = DockingLayout.Place(new Rectangle(10, 10, 200, 200), new Size(500, 900), new Rectangle(0, 0, 400, 600), 4);
        check(result == new Rectangle(0, 0, 400, 600), "oversized followers fit within a smaller monitor's working area");
        bool rejected = false; try { DockingLayout.Place(Rectangle.Empty, new Size(370, 680), area, 4); } catch (ArgumentException) { rejected = true; }
        check(rejected, "invalid target bounds cannot produce an off-screen dock position");

        Host host = new Host(); WindowDockingSession session = new WindowDockingSession(host);
        check(session.Update(4) == DockingUpdate.Inactive && host.Moves == 0, "a fresh docking session does not move the user's floating window");
        check(!session.Attach(IntPtr.Zero) && !session.Attach(new IntPtr(8)) && !session.IsAttached, "missing and invalid window handles cannot be attached");
        check(session.Attach(new IntPtr(7)) && session.Target == new IntPtr(7), "docking binds one explicitly selected external window");
        check(session.Update(4) == DockingUpdate.Following && host.Bounds == new Rectangle(704, 120, 370, 680), "attached follower tracks the target without changing target dimensions");
        int moves = host.Moves; session.Update(4);
        check(host.Moves == moves, "stationary windows do not trigger repeated repositioning");
        host.Target.Bounds = new Rectangle(170, 200, 720, 500); session.Update(4);
        check(host.Bounds.Location == new Point(894, 200), "moving and resizing the bound window updates the attached location");
        host.Target.Minimized = true;
        check(session.Update(4) == DockingUpdate.Hidden && session.HiddenByTarget && host.Hides == 1 && session.IsAttached, "minimizing the target hides the follower while preserving its binding");
        session.Update(4);
        check(host.Hides == 1 && host.Moves == moves + 1, "minimized targets neither repeatedly hide nor move the follower to minimized coordinates");
        host.Target.Minimized = false; session.Update(4);
        check(host.Shows == 1 && !session.HiddenByTarget && session.IsAttached, "restoring the target brings back the follower using the existing binding");
        host.Target.Visible = false; session.Update(4); host.Target.Visible = true; session.Update(4);
        check(host.Hides == 2 && host.Shows == 2, "hiding and revealing a target also hides and restores its attached follower");
        host.Target.WorkingArea = new Rectangle(-1920, 0, 1920, 1080); host.Target.Bounds = new Rectangle(-1600, 80, 800, 600); session.Update(4);
        check(host.Bounds.X == -796 && host.Bounds.Y == 80, "a bound window moving between monitors uses the new working area");
        check(!session.Attach(new IntPtr(99)) && session.Target == new IntPtr(7), "an invalid reattachment attempt cannot replace an existing binding");
        host.Target.Visible = false; session.Update(4); host.Exists = false;
        check(session.Update(4) == DockingUpdate.Detached && !session.IsAttached && !session.HiddenByTarget && host.Shows == 3, "closing a hidden target detaches and restores the follower so it cannot become lost");
        host.Exists = true; host.Target.Visible = true; session.Attach(new IntPtr(7)); host.Target.ProcessId = 43;
        check(session.Update(4) == DockingUpdate.Detached && !session.IsAttached, "a reused handle owned by another process cannot inherit the previous docking or send binding");
        host.Target.ProcessId = 42; session.Attach(new IntPtr(7)); host.Target.Minimized = true; session.Update(4); session.Detach(); int shows = host.Shows; session.Detach();
        check(!session.IsAttached && !session.HiddenByTarget && host.Shows == shows, "manual detach restores a hidden follower once and repeated detach is harmless");
        host.Target.Minimized = false; moves = host.Moves; session.Update(4);
        check(host.Moves == moves, "detaching stops all automatic movement until the user explicitly attaches again");
    }
}
