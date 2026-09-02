namespace KPod.Tests;

/// <summary>
/// Replacement for JUnit's <c>@TempDir</c>: a unique directory that is deleted
/// when the test finishes.
/// </summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kpod-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute path of the temporary directory.</summary>
    public string Path { get; }

    public string Resolve(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    /// <summary>Writes a file under the temp dir, creating parent directories.</summary>
    public string WriteFile(string relativePath, string contents)
    {
        string full = Resolve(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return full;
    }

    /// <summary>Writes a binary file under the temp dir, creating parent directories.</summary>
    public string WriteFile(string relativePath, byte[] contents)
    {
        string full = Resolve(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, contents);
        return full;
    }

    public string CreateDirectory(string relativePath)
    {
        string full = Resolve(relativePath.Split('/'));
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; a leaked temp directory must never fail a test.
        }
    }
}
