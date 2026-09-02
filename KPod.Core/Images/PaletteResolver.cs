using KPod.Core.Pods;

namespace KPod.Core.Images;

/// <summary>One selectable palette, with the label the UI shows for it.</summary>
public sealed record PaletteChoice(string Label, int[] Palette);

/// <summary>The palettes offered for a RAW preview, and which one starts selected.</summary>
public sealed record PaletteChoices(IReadOnlyList<PaletteChoice> Choices, int DefaultIndex);

/// <summary>
/// Finds the palette to draw a RAW or CLR entry with. Pure archive logic, kept out
/// of the UI so it can be tested without Windows.
/// </summary>
public static class PaletteResolver
{
    private const string VgaPalette = "VGA.ACT";
    private const string MetalPalette = "METALCR2.ACT";

    /// <summary>
    /// Resolves the palette for a RAW or CLR entry, in priority order:
    /// <list type="number">
    ///   <item>the palette named in the entry's own directory field</item>
    ///   <item>the same base name with an <c>.act</c> extension</item>
    ///   <item>any <c>.act</c> in the same archive directory</item>
    ///   <item><c>VGA.ACT</c> anywhere in the archive</item>
    ///   <item><c>METALCR2.ACT</c> anywhere in the archive</item>
    ///   <item>the bundled <c>metalcr2.act</c> resource</item>
    ///   <item>greyscale</item>
    /// </list>
    ///
    /// <para>Step one matters more than its position suggests. Where the packer
    /// wrote a palette name, the rules below it are close to useless: on
    /// Hellbender's <c>GAME.POD</c>, Fury3's <c>FURY3.POD</c>, MTM1's
    /// <c>GAME.POD</c> and Terminal Velocity's <c>CDROM.POD</c> the stored name
    /// disagrees with the same-directory guess for all but three of 8 224 RAW
    /// entries, because that guess just takes whichever <c>.ACT</c> happens to
    /// come first in the archive.</para>
    /// </summary>
    /// <param name="entryName">Archive path of the RAW or CLR entry.</param>
    /// <param name="archive">Archive to resolve against, or null.</param>
    /// <param name="rawNameField">
    /// The entry's directory field, when the caller has it. Supplying it is what
    /// enables step one.
    /// </param>
    public static int[] Resolve(string entryName, PodArchive? archive, byte[]? rawNameField = null)
    {
        if (archive is not null)
        {
            PodEntry? stored = FindStoredPalette(archive, rawNameField);
            if (stored is not null)
            {
                return TryDecodeAct(archive.GetEntryBytes(stored));
            }

            int dot = entryName.LastIndexOf('.');
            if (dot > 0)
            {
                PodEntry? exact = archive.FindEntry(entryName.Substring(0, dot) + ".act");
                if (exact is not null)
                {
                    return TryDecodeAct(archive.GetEntryBytes(exact));
                }
            }

            int slash = Math.Max(entryName.LastIndexOf('\\'), entryName.LastIndexOf('/'));
            string directoryPrefix = slash >= 0
                ? entryName.Substring(0, slash + 1).ToUpperInvariant()
                : string.Empty;
            foreach (PodEntry entry in archive.Entries)
            {
                string upper = entry.Name.ToUpperInvariant();
                if (upper.StartsWith(directoryPrefix, StringComparison.Ordinal)
                    && upper.EndsWith(".ACT", StringComparison.Ordinal))
                {
                    return TryDecodeAct(archive.GetEntryBytes(entry));
                }
            }

            PodEntry? vga = FindArchivePalette(archive, VgaPalette);
            if (vga is not null)
            {
                return TryDecodeAct(archive.GetEntryBytes(vga));
            }

            PodEntry? metal = FindArchivePalette(archive, MetalPalette);
            if (metal is not null)
            {
                return TryDecodeAct(archive.GetEntryBytes(metal));
            }
        }

        return RawImageDecoder.LoadResourcePalette();
    }

    /// <summary>
    /// The palette list offered when the user has to pick dimensions by hand: the
    /// same-name ACT first when it exists, then VGA, then every other ACT in the
    /// archive, then METALCR2, greyscale, and the bundled palette.
    /// </summary>
    public static PaletteChoices ResolveChoices(
        string entryName, PodArchive? archive, byte[]? rawNameField = null)
    {
        List<PaletteChoice> choices = [];
        int defaultIndex = -1;

        if (archive is not null)
        {
            PodEntry? stored = FindStoredPalette(archive, rawNameField);
            if (stored is not null)
            {
                choices.Add(new PaletteChoice(
                    "Stored in the archive: " + stored.Name,
                    TryDecodeAct(archive.GetEntryBytes(stored))));
                defaultIndex = 0;
            }

            int dot = entryName.LastIndexOf('.');
            if (dot > 0)
            {
                PodEntry? sameName = archive.FindEntry(entryName.Substring(0, dot) + ".act");
                if (sameName is not null)
                {
                    choices.Add(new PaletteChoice(
                        "Same-name ACT: " + sameName.Name,
                        TryDecodeAct(archive.GetEntryBytes(sameName))));
                    if (defaultIndex < 0)
                    {
                        defaultIndex = choices.Count - 1;
                    }
                }
            }

            PodEntry? vga = FindArchivePalette(archive, VgaPalette);
            if (vga is not null)
            {
                choices.Add(new PaletteChoice(VgaPalette, TryDecodeAct(archive.GetEntryBytes(vga))));
                if (defaultIndex < 0)
                {
                    defaultIndex = choices.Count - 1;
                }
            }

            foreach (PodEntry entry in archive.Entries)
            {
                string upper = entry.Name.ToUpperInvariant();
                if (!upper.EndsWith(".ACT", StringComparison.Ordinal)
                    || upper.EndsWith(MetalPalette, StringComparison.Ordinal)
                    || upper.EndsWith(VgaPalette, StringComparison.Ordinal))
                {
                    continue;
                }

                choices.Add(new PaletteChoice(
                    "Archive ACT: " + entry.Name,
                    TryDecodeAct(archive.GetEntryBytes(entry))));
            }

            PodEntry? metal = FindArchivePalette(archive, MetalPalette);
            if (metal is not null)
            {
                choices.Add(new PaletteChoice(MetalPalette, TryDecodeAct(archive.GetEntryBytes(metal))));
            }
        }

        int greyscaleIndex = choices.Count;
        choices.Add(new PaletteChoice("Greyscale", RawImageDecoder.GreyscalePalette()));
        if (defaultIndex < 0)
        {
            defaultIndex = greyscaleIndex;
        }

        bool hasMetal = false;
        foreach (PaletteChoice choice in choices)
        {
            if (choice.Label == MetalPalette)
            {
                hasMetal = true;
                break;
            }
        }

        if (!hasMetal)
        {
            choices.Add(new PaletteChoice("Bundled " + MetalPalette, RawImageDecoder.LoadResourcePalette()));
        }

        return new PaletteChoices(choices, defaultIndex);
    }

    /// <summary>
    /// The archive entry for the palette named in a directory field, or null when
    /// the field names none or the archive does not hold it.
    /// </summary>
    private static PodEntry? FindStoredPalette(PodArchive archive, byte[]? rawNameField)
    {
        string? palette = PodNameField.SecondString(rawNameField);
        if (palette is null || !palette.EndsWith(".ACT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindArchivePalette(archive, palette);
    }

    private static PodEntry? FindArchivePalette(PodArchive archive, string fileName) =>
        archive.FindEntry(fileName) ?? archive.FindEntryByTitle(fileName);

    private static int[] TryDecodeAct(byte[] bytes)
    {
        try
        {
            return RawImageDecoder.DecodeAct(bytes);
        }
        catch (ArgumentException)
        {
            return RawImageDecoder.GreyscalePalette();
        }
    }
}
