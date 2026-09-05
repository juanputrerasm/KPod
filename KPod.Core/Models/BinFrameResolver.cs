using KPod.Core.Pods;

namespace KPod.Core.Models;

/// <summary>Where an animated BIN's frame models live in the archive.</summary>
public sealed record BinFrame(string Name, PodEntry? Entry);

/// <summary>
/// Resolves the models an animated BIN names as its frames.
///
/// <para>Frames are stored as bare file names, so they are looked up the way the game
/// looks them up: in <c>MODELS</c> first, then anywhere in the archive. A frame that
/// resolves from another pod entirely is invisible here, which is exactly the case
/// that makes an animation work for its author and fail for everyone else.</para>
/// </summary>
public static class BinFrameResolver
{
    private static readonly string[] SearchFolders = ["MODELS", "ART", "DATA"];

    public static IReadOnlyList<BinFrame> Resolve(PodArchive? archive, BinModel model)
    {
        if (model.FrameNames.Count == 0) return [];

        List<BinFrame> frames = new(model.FrameNames.Count);
        foreach (string name in model.FrameNames)
        {
            frames.Add(new BinFrame(name, archive is null ? null : Find(archive, name)));
        }

        return frames;
    }

    private static PodEntry? Find(PodArchive archive, string name)
    {
        string title = name.IndexOf('.') >= 0 ? name : name + ".BIN";
        foreach (string folder in SearchFolders)
        {
            PodEntry? entry = archive.FindEntry(folder + "\\" + title)
                ?? archive.FindEntry(folder + "/" + title);
            if (entry is not null) return entry;
        }

        return archive.FindEntry(title) ?? archive.FindEntryByTitle(title);
    }
}
