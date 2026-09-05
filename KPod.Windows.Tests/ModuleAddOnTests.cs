using KPod.Windows.Platform;

namespace KPod.Windows.Tests;

/// <summary>The add-on probe: which of the five DLLs is missing, and where it looks for them.</summary>
public class ModuleAddOnTests
{
    [Fact]
    public void EmptyFolderReportsTheFirstMissingDll()
    {
        string folder = NewFolder();
        try
        {
            Assert.Equal(ModuleNativeLibrary.RequiredFiles[0], ModuleNativeLibrary.MissingFile(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void PartialInstallNamesTheOneThatIsAbsent()
    {
        string folder = NewFolder();
        try
        {
            foreach (string name in ModuleNativeLibrary.RequiredFiles)
            {
                if (name != "openmpt-vorbis.dll") File.WriteAllBytes(Path.Combine(folder, name), []);
            }

            Assert.Equal("openmpt-vorbis.dll", ModuleNativeLibrary.MissingFile(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void CompleteFolderReportsNothingMissing()
    {
        string folder = NewFolder();
        try
        {
            foreach (string name in ModuleNativeLibrary.RequiredFiles)
                File.WriteAllBytes(Path.Combine(folder, name), []);

            Assert.Null(ModuleNativeLibrary.MissingFile(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void FiveDllsAreRequiredAndLibopenmptIsLoadedLast()
    {
        Assert.Equal(5, ModuleNativeLibrary.RequiredFiles.Length);
        Assert.Equal("libopenmpt.dll", ModuleNativeLibrary.RequiredFiles[ModuleNativeLibrary.RequiredFiles.Length - 1]);
    }

    [Fact]
    public void AddOnIsLookedForBesideTheExecutable()
    {
        Assert.Equal(AppContext.BaseDirectory, ModuleNativeLibrary.Folder);
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "KPodAddOn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
