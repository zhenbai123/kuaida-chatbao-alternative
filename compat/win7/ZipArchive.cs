using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using LegacyZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

// A small adapter for the archive API used by the shared phrase import/export.
// Entries are read as streams; paths are checked by LibraryTransfer before use.
namespace System.IO.Compression
{
    public enum ZipArchiveMode { Read, Create }
    public enum CompressionLevel { Optimal, Fastest, NoCompression }
    public sealed class ZipArchive : IDisposable
    {
        private readonly Stream stream;
        private readonly LegacyZipFile input;
        private readonly ZipOutputStream output;
        private EntryWriter active;
        private bool disposed;
        public List<ZipArchiveEntry> Entries { get; private set; }
        public ZipArchive(Stream source, ZipArchiveMode mode)
        {
            stream = source; Entries = new List<ZipArchiveEntry>();
            if (mode == ZipArchiveMode.Read)
            {
                input = new LegacyZipFile(source); input.IsStreamOwner = false;
                foreach (ZipEntry entry in input) Entries.Add(new ZipArchiveEntry(this, entry));
            }
            else { output = new ZipOutputStream(source); output.IsStreamOwner = false; output.SetLevel(6); }
        }
        public ZipArchiveEntry CreateEntry(string name) { return CreateEntry(name, CompressionLevel.Optimal); }
        public ZipArchiveEntry CreateEntry(string name, CompressionLevel level)
        {
            if (disposed || output == null) throw new InvalidOperationException();
            if (active != null) active.Dispose();
            output.SetLevel(level == CompressionLevel.NoCompression ? 0 : level == CompressionLevel.Fastest ? 1 : 6);
            var entry = new ZipEntry(name); entry.IsUnicodeText = true;
            var wrapper = new ZipArchiveEntry(this, entry); Entries.Add(wrapper); return wrapper;
        }
        internal Stream Open(ZipArchiveEntry entry)
        {
            if (disposed) throw new ObjectDisposedException("ZipArchive");
            if (input != null) return input.GetInputStream(entry.Entry);
            if (active != null) active.Dispose();
            output.PutNextEntry(entry.Entry); active = new EntryWriter(output); return active;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { if (input != null) input.Close(); if (output != null) { if (active != null) active.Dispose(); output.Finish(); output.Close(); } }
            finally { stream.Dispose(); }
        }
        private sealed class EntryWriter : Stream
        {
            private readonly ZipOutputStream output; private bool closed;
            internal EntryWriter(ZipOutputStream target) { output = target; }
            public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return !closed; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { output.Flush(); } public override void Write(byte[] b, int o, int n) { if (closed) throw new ObjectDisposedException("ZIP entry"); output.Write(b, o, n); }
            public override int Read(byte[] b, int o, int n) { throw new NotSupportedException(); } public override long Seek(long o, SeekOrigin w) { throw new NotSupportedException(); } public override void SetLength(long n) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (!closed) { closed = true; if (disposing) output.CloseEntry(); } base.Dispose(disposing); }
        }
    }
    public sealed class ZipArchiveEntry
    {
        private readonly ZipArchive archive; internal readonly ZipEntry Entry;
        internal ZipArchiveEntry(ZipArchive owner, ZipEntry entry) { archive = owner; Entry = entry; }
        public string FullName { get { return Entry.Name; } }
        public long Length { get { return Entry.Size; } }
        public Stream Open() { return archive.Open(this); }
    }
}
