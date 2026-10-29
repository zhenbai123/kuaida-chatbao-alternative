using System;
using System.Collections.Generic;
using System.ComponentModel;
#if !WIN7_HEADLESS_TESTS
using System.Drawing;
#endif
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
#if !WIN7_HEADLESS_TESTS
using System.Windows.Forms;
#endif

namespace QuickReplyLite
{
    // The Win7 build uses CLR 2.0/.NET 3.5.1 already included with Windows 7.
    internal static class LegacyText
    {
        internal static bool IsBlank(string value) { if (value == null) return true; foreach (char c in value) if (!Char.IsWhiteSpace(c)) return false; return true; }
        internal static string Join<T>(string separator, IEnumerable<T> values)
        { var items = new List<string>(); foreach (T value in values) items.Add(Convert.ToString(value)); return String.Join(separator, items.ToArray()); }
        internal static string Concat(IEnumerable<string> values) { return Join("", values); }
    }
    internal static class LegacyIO
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushFileBuffers(IntPtr file);
        internal static string Combine(params string[] paths) { string result = paths[0]; for (int i = 1; i < paths.Length; i++) result = Path.Combine(result, paths[i]); return result; }
        internal static void Copy(Stream source, Stream target) { byte[] buffer = new byte[65536]; int read; while ((read = source.Read(buffer, 0, buffer.Length)) > 0) target.Write(buffer, 0, read); }
        internal static void Flush(FileStream stream) { stream.Flush(); if (Environment.OSVersion.Platform == PlatformID.Win32NT && !FlushFileBuffers(stream.SafeFileHandle.DangerousGetHandle())) throw new IOException("无法保存话术到磁盘。", new Win32Exception(Marshal.GetLastWin32Error())); }
        internal static void CopyTo(this Stream source, Stream target) { Copy(source, target); }
        internal static StringBuilder Clear(this StringBuilder text) { text.Length = 0; return text; }
    }
    internal sealed class Win7Cancellation : IDisposable
    {
        private volatile bool cancelled;
        internal Win7Cancellation Token { get { return this; } }
        internal void Cancel() { cancelled = true; }
        internal void ThrowIfCancellationRequested() { if (cancelled) throw new OperationCanceledException(); }
        public void Dispose() { Cancel(); }
    }
#if !WIN7_HEADLESS_TESTS
    internal static class LegacyUi
    {
        private static float dpi;
        internal static float Dpi(Control control) { if (dpi <= 0) { using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) dpi = graphics.DpiX; } return dpi; }
        internal static void Delay(int milliseconds, Win7Cancellation token)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            do { token.ThrowIfCancellationRequested(); Application.DoEvents(); token.ThrowIfCancellationRequested(); int remaining = milliseconds - (int)timer.ElapsedMilliseconds; if (remaining > 0) Thread.Sleep(Math.Min(10, remaining)); } while (timer.ElapsedMilliseconds < milliseconds);
        }
        // File import/export runs on a worker; only the UI thread pumps messages.
        // Clipboard writes and synthetic input remain on the original STA thread.
        internal static T Run<T>(Func<T> work)
        {
            T value = default(T); Exception error = null;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            {
                ThreadPool.QueueUserWorkItem(delegate { try { value = work(); } catch (Exception ex) { error = ex; } finally { completed.Set(); } });
                while (!completed.WaitOne(15, false)) Application.DoEvents();
            }
            if (error != null) throw new IOException(error.Message, error);
            return value;
        }
    }
#endif
}
