using System;
using System.Drawing;
using QuickReplyLite;

internal static class WindowDockingTests
{
    private sealed class Host : IWindowDockingHost
    {
        internal DockingTarget Target = new DockingTarget { ProcessId = 42, Visible = true, Bounds = new Rectangle(100, 120, 600, 500) };
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
        Rectangle flush = DockingLayout.Place(rightTarget, 370);
        check(flush.Left == rightTarget.Right && flush.Top == rightTarget.Top && flush.Bottom == rightTarget.Bottom, "docking touches the right edge and matches both vertical edges");
        Rectangle edgeTarget = new Rectangle(1200, 180, 700, 600);
        flush = DockingLayout.Place(edgeTarget, 370);
        check(flush == new Rectangle(1900, 180, 370, 600) && flush.Right > area.Right, "insufficient screen space never flips docking to the left or clamps it inside the chat");
        Rectangle negativeTarget = new Rectangle(-1700, -180, 900, 700);
        flush = DockingLayout.Place(negativeTarget, 370);
        check(flush == new Rectangle(-800, -180, 370, 700), "docking supports monitors to the left and above the primary display without shifting vertically");
        Rectangle result = DockingLayout.Place(area, 370);
        check(result == new Rectangle(1920, 0, 370, 1040), "a maximized target retains its outside-right attachment instead of overlaying the chat");
        result = DockingLayout.Place(new Rectangle(20, 900, 900, 300), 370);
        check(result.Top == 900 && result.Bottom == 1200, "a partially off-screen target keeps both vertical edges aligned");
        result = DockingLayout.Place(new Rectangle(10, 10, 200, 200), 500);
        check(result == new Rectangle(210, 10, 500, 200), "follower width is retained independently of the target size or available screen space");
        bool rejected = false; try { DockingLayout.Place(Rectangle.Empty, 370); } catch (ArgumentException) { rejected = true; }
        check(rejected, "invalid target bounds are rejected");
        rejected = false; try { DockingLayout.Place(rightTarget, 0); } catch (ArgumentException) { rejected = true; }
        check(rejected, "invalid follower width is rejected");

        Host host = new Host(); WindowDockingSession session = new WindowDockingSession(host);
        check(session.Update() == DockingUpdate.Inactive && host.Moves == 0, "a fresh docking session does not move the user's floating window");
        check(!session.Attach(IntPtr.Zero) && !session.Attach(new IntPtr(8)) && !session.IsAttached, "missing and invalid window handles cannot be attached");
        check(session.Attach(new IntPtr(7)) && session.Target == new IntPtr(7), "docking binds one explicitly selected external window");
        check(session.Update() == DockingUpdate.Following && host.Bounds == new Rectangle(700, 120, 370, 500), "attachment aligns the follower's height without changing the target window");
        int moves = host.Moves; session.Update();
        check(host.Moves == moves, "stationary windows do not trigger repeated repositioning");
        host.Target.Bounds = new Rectangle(170, 200, 720, 500); session.Update();
        check(host.Bounds.Location == new Point(890, 200), "moving and resizing the bound window updates the attached location");
        host.Target.Minimized = true;
        check(session.Update() == DockingUpdate.Hidden && session.HiddenByTarget && host.Hides == 1 && session.IsAttached, "minimizing the target hides the follower while preserving its binding");
        session.Update();
        check(host.Hides == 1 && host.Moves == moves + 1, "minimized targets neither repeatedly hide nor move the follower to minimized coordinates");
        host.Target.Minimized = false; session.Update();
        check(host.Shows == 1 && !session.HiddenByTarget && session.IsAttached, "restoring the target brings back the follower using the existing binding");
        host.Target.Visible = false; session.Update(); host.Target.Visible = true; session.Update();
        check(host.Hides == 2 && host.Shows == 2, "hiding and revealing a target also hides and restores its attached follower");
        host.Target.Bounds = new Rectangle(-1600, 80, 800, 600); session.Update();
        check(host.Bounds == new Rectangle(-800, 80, 370, 600), "moving between monitors retains the right-hand attachment and follows the target height");
        host.Bounds.Width = 440; host.Target.Bounds.Height = 720; session.Update();
        check(host.Bounds == new Rectangle(-800, 80, 440, 720), "resizing the target follows its height while retaining the user's chosen follower width");
        check(!session.Attach(new IntPtr(99)) && session.Target == new IntPtr(7), "an invalid reattachment attempt cannot replace an existing binding");
        host.Target.Visible = false; session.Update(); host.Exists = false;
        check(session.Update() == DockingUpdate.Detached && !session.IsAttached && !session.HiddenByTarget && host.Shows == 3, "closing a hidden target detaches and restores the follower so it cannot become lost");
        host.Exists = true; host.Target.Visible = true; session.Attach(new IntPtr(7)); host.Target.ProcessId = 43;
        check(session.Update() == DockingUpdate.Detached && !session.IsAttached, "a reused handle owned by another process cannot inherit the previous docking or send binding");
        host.Target.ProcessId = 42; session.Attach(new IntPtr(7)); host.Target.Minimized = true; session.Update(); session.Detach(); int shows = host.Shows; session.Detach();
        check(!session.IsAttached && !session.HiddenByTarget && host.Shows == shows, "manual detach restores a hidden follower once and repeated detach is harmless");
        host.Target.Minimized = false; moves = host.Moves; session.Update();
        check(host.Moves == moves, "detaching stops all automatic movement until the user explicitly attaches again");
    }
}
