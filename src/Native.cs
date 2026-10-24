using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickReplyLite
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public UNION data; }
        [StructLayout(LayoutKind.Explicit)] private struct UNION
        {
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint message; public ushort low, high; }
        internal static uint ProcessId(IntPtr window) { uint id; GetWindowThreadProcessId(window, out id); return id; }
        internal static bool IsOwnWindow(IntPtr window) { return ProcessId(window) == (uint)Process.GetCurrentProcess().Id; }

        private static INPUT Key(ushort key, bool up)
        { return new INPUT { type = 1, data = new UNION { keyboard = new KEYBDINPUT { vk = key, flags = up ? 2u : 0u } } }; }

        private static void Keys(INPUT[] keys)
        {
            if (SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(INPUT))) != (uint)keys.Length)
            {
                INPUT[] releases = { Key(0x56, true), Key(0x11, true), Key(0x0D, true) };
                SendInput((uint)releases.Length, releases, Marshal.SizeOf(typeof(INPUT)));
                throw new InvalidOperationException("系统未允许键盘操作，请让快答和聊天软件都以普通用户权限运行。");
            }
        }

        internal static async Task FocusTarget(IntPtr target, CancellationToken token)
        {
            if (target == IntPtr.Zero || !IsWindow(target) || IsOwnWindow(target))
                throw new InvalidOperationException("请先点击聊天软件的输入框，再点箭头发送或双击话术仅粘贴。");
            if (IsIconic(target)) ShowWindow(target, 9);
            if (GetForegroundWindow() != target) SetForegroundWindow(target);
            await Task.Delay(140, token);
            if (GetForegroundWindow() != target)
                throw new InvalidOperationException("无法切回目标窗口。请重新点选聊天输入框后再发送。");
            for (int i = 0; i < 12; i++)
            {
                if ((GetAsyncKeyState(0x10) & 0x8000) == 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                    (GetAsyncKeyState(0x12) & 0x8000) == 0 && (GetAsyncKeyState(0x5B) & 0x8000) == 0 && (GetAsyncKeyState(0x5C) & 0x8000) == 0) return;
                await Task.Delay(80, token);
            }
            throw new InvalidOperationException("请松开 Ctrl、Alt、Shift 或 Win 键后再发送。");
        }

        internal static void Paste(IntPtr target)
        {
            if (GetForegroundWindow() != target) throw new InvalidOperationException("目标窗口已切换，已停止发送。");
            Keys(new INPUT[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) });
        }

        internal static bool Confirm(IntPtr target, bool allowOwnedDialog)
        {
            IntPtr current = GetForegroundWindow();
            bool related = current == target || (allowOwnedDialog && IsWindow(current) &&
                ProcessId(current) == ProcessId(target) && GetAncestor(current, 3) == GetAncestor(target, 3));
            if (!related) return false;
            Keys(new INPUT[] { Key(0x0D, false), Key(0x0D, true) });
            return true;
        }

        internal static async Task WriteClipboard(Action write, CancellationToken token)
        {
            for (int i = 0; i < 6; i++)
            {
                token.ThrowIfCancellationRequested();
                try { write(); return; }
                catch (ExternalException) { if (i == 5) throw new InvalidOperationException("剪贴板正在被占用，请稍后再试。"); }
                await Task.Delay(80, token);
            }
        }

        internal static bool IsImage(string file)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp";
        }

        internal static void PutFiles(string[] paths)
        {
            if (paths.Length == 1 && IsImage(paths[0]))
            {
                try { using (Image original = Image.FromFile(paths[0])) using (Bitmap bitmap = new Bitmap(original)) { Clipboard.SetImage(bitmap); return; } }
                catch (ArgumentException) { /* Unreadable image: send the original file instead. */ }
            }
            StringCollection files = new StringCollection(); files.AddRange(paths); Clipboard.SetFileDropList(files);
        }
    }
}
