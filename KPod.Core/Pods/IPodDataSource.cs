namespace KPod.Core.Pods;

/// <summary>
/// Random-access byte source behind an open archive.
///
/// <para>A POD directory is a few kilobytes even when the archive is hundreds of
/// megabytes, so the reader parses the directory and leaves the payloads where they
/// are. Everything that later wants an entry's bytes, a preview, an extract or a
/// re-save, asks the source for exactly the range it needs.</para>
///
/// <para>Implementations must be safe to call from more than one thread: previews
/// and extraction run on background tasks while the UI thread is still reading
/// sizes and names.</para>
/// </summary>
public interface IPodDataSource : IDisposable
{
    /// <summary>Total bytes available, which is the archive's size on disk.</summary>
    long Length { get; }

    /// <summary>
    /// The requested range as a new array.
    /// </summary>
    /// <exception cref="PodFormatException">The range runs past the end of the source.</exception>
    byte[] ReadExact(long offset, int count);

    /// <summary>
    /// Fills <paramref name="buffer"/> from <paramref name="offset"/>, returning how
    /// many bytes were actually available. A short return means end of source, not
    /// an error.
    /// </summary>
    int Read(long offset, byte[] buffer, int bufferOffset, int count);

    /// <summary>
    /// Streams a range to <paramref name="target"/> through <paramref name="scratch"/>,
    /// so a large payload never has to exist as one array.
    /// </summary>
    void CopyTo(long offset, long count, Stream target, byte[] scratch);
}

/// <summary>
/// Reads an archive straight from the file it lives in, one handle held open for as
/// long as the archive is.
///
/// <para>The handle allows read, write and delete sharing. On Windows that is what
/// lets the user save over the archive they currently have open: the writer builds a
/// temporary file beside the target and replaces it, and nothing takes an exclusive
/// lock on the bytes this source is still reading.</para>
/// </summary>
public sealed class FilePodDataSource : IPodDataSource
{
    /// <summary>
    /// Buffering is disabled. Every read here is an explicit, already-sized range,
    /// and a stream buffer would only be discarded by the seek that precedes it.
    /// </summary>
    private const int NoBuffering = 1;

    private readonly object _gate = new();
    private readonly FileStream _stream;
    private bool _disposed;

    public FilePodDataSource(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, NoBuffering, FileOptions.RandomAccess);
        Length = _stream.Length;
    }

    /// <summary>The file this source reads from.</summary>
    public string Path { get; }

    public long Length { get; }

    public byte[] ReadExact(long offset, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        byte[] buffer = new byte[count];
        int read = Read(offset, buffer, 0, count);
        if (read != count)
        {
            throw new PodFormatException(
                $"POD data ends early: wanted {count} bytes at {offset}, got {read}");
        }

        return buffer;
    }

    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (offset < 0 || count <= 0)
        {
            return 0;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FilePodDataSource));
            }

            _stream.Seek(offset, SeekOrigin.Begin);
            int total = 0;
            while (total < count)
            {
                int chunk = _stream.Read(buffer, bufferOffset + total, count - total);
                if (chunk <= 0)
                {
                    break;
                }

                total += chunk;
            }

            return total;
        }
    }

    public void CopyTo(long offset, long count, Stream target, byte[] scratch) =>
        PodDataSource.CopyThrough(this, offset, count, target, scratch);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
        }
    }
}

/// <summary>
/// An archive that is already in memory: a test fixture, a manifest build, or bytes
/// handed in by a caller. Holds no file handle, so disposing it does nothing.
/// </summary>
public sealed class MemoryPodDataSource(byte[] bytes) : IPodDataSource
{
    private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

    public long Length => _bytes.Length;

    /// <summary>The backing array, so callers holding the whole archive can avoid a copy.</summary>
    public byte[] Bytes => _bytes;

    public byte[] ReadExact(long offset, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (offset < 0 || offset + count > _bytes.Length)
        {
            throw new PodFormatException(
                $"POD data ends early: wanted {count} bytes at {offset}, have {_bytes.Length}");
        }

        byte[] buffer = new byte[count];
        Array.Copy(_bytes, (int)offset, buffer, 0, count);
        return buffer;
    }

    public int Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        if (offset < 0 || offset >= _bytes.Length || count <= 0)
        {
            return 0;
        }

        int available = (int)Math.Min(count, _bytes.Length - offset);
        Array.Copy(_bytes, (int)offset, buffer, bufferOffset, available);
        return available;
    }

    public void CopyTo(long offset, long count, Stream target, byte[] scratch) =>
        PodDataSource.CopyThrough(this, offset, count, target, scratch);

    public void Dispose()
    {
        // Nothing to release.
    }
}

/// <summary>Shared helpers over <see cref="IPodDataSource"/>.</summary>
public static class PodDataSource
{
    /// <summary>The transfer size used wherever a payload is streamed rather than held.</summary>
    public const int ScratchSize = 65536;

    /// <summary>A buffer of the standard transfer size.</summary>
    public static byte[] NewScratch() => new byte[ScratchSize];

    /// <summary>Streams a range from a source to a stream, reusing one buffer.</summary>
    public static void CopyThrough(IPodDataSource source, long offset, long count,
        Stream target, byte[] scratch)
    {
        long remaining = count;
        long at = offset;
        while (remaining > 0)
        {
            int want = (int)Math.Min(scratch.Length, remaining);
            int read = source.Read(at, scratch, 0, want);
            if (read <= 0)
            {
                break;
            }

            target.Write(scratch, 0, read);
            at += read;
            remaining -= read;
        }
    }
}
