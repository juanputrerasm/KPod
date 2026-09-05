using KPod.Windows.Platform;

namespace KPod.Windows.Tests;

/// <summary>
/// The optional software renderer: an opengl32.dll beside the executable replaces the
/// system one for this process. Absent, nothing changes and nothing is reported.
/// </summary>
public class OpenGlAddOnTests
{
    [Fact]
    public void RendererIsLookedForBesideTheExecutable()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "opengl32.dll"), OpenGlNativeLibrary.CandidatePath);
    }

    [Fact]
    public void ProbingIsSilentAndRepeatableWhenNoRendererIsInstalled()
    {
        // Repeated because the probe runs once per process and must be safe to call from
        // every BIN preview that opens.
        OpenGlNativeLibrary.PreferLocalRenderer();
        OpenGlNativeLibrary.PreferLocalRenderer();

        // A load can still fail on a file that is present, so only the absent case has a
        // single right answer.
        if (!File.Exists(OpenGlNativeLibrary.CandidatePath))
        {
            Assert.False(OpenGlNativeLibrary.UsingLocalRenderer);
            Assert.Null(OpenGlNativeLibrary.Status);
        }
    }

    [Fact]
    public void APresentButUnusableRendererIsNeverSilent()
    {
        // Whatever happened to a file that is actually there, the user gets told: a DLL
        // that looks installed and does nothing is the worst outcome to leave unexplained.
        if (!File.Exists(OpenGlNativeLibrary.CandidatePath)) return;

        OpenGlNativeLibrary.PreferLocalRenderer();

        Assert.False(string.IsNullOrWhiteSpace(OpenGlNativeLibrary.Status));
    }
}
