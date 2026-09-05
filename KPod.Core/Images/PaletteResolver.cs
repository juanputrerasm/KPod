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
    ///   <item><c>METALCR2.ACT</c> anywhere in the archive</item>
    ///   <item><c>VGA.ACT</c> anywhere in the archive</item>
    ///   <item>the bundled <c>metalcr2.act</c> resource</item>
    /// </list>
    ///
    /// <para>The archive's own <c>METALCR2.ACT</c> outranks the bundled copy because
    /// they are not the same file: CPR ships a different METALCR2 from MTM1 and MTM2,
    /// and taking the one the pod carries is right whichever game it came from. A
    /// <c>VGA.ACT</c> in the pod says the archive belongs to one of the flight games,
    /// and since nothing distinguishes Terminal Velocity from Fury3 or Hellbender at
    /// this point, that copy is the only one that can be trusted.</para>
    ///
    /// <para>An <c>.act</c> whose name matches nothing is never guessed at. Picking
    /// whichever differently-named palette happened to sit in the same folder was a
    /// coin toss dressed up as a rule; those are offered last in the choice list and
    /// chosen by hand.</para>
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

            PodEntry? metal = FindArchivePalette(archive, MetalPalette);
            if (metal is not null)
            {
                return TryDecodeAct(archive.GetEntryBytes(metal));
            }

            PodEntry? vga = FindArchivePalette(archive, VgaPalette);
            if (vga is not null)
            {
                return TryDecodeAct(archive.GetEntryBytes(vga));
            }
        }

        return RawImageDecoder.LoadResourcePalette();
    }

    /// <summary>
    /// The complete palette list offered by every RAW preview. Its default follows
    /// <see cref="Resolve"/>, the four bundled game palettes and greyscale are always
    /// available, and every other <c>.act</c> in the archive is offered last for
    /// correcting an ambiguous archive by hand.
    /// </summary>
    public static PaletteChoices ResolveChoices(
        string entryName, PodArchive? archive, byte[]? rawNameField = null)
    {
        List<PaletteChoice> choices = [];
        int defaultIndex = -1;
        HashSet<string> addedEntries = new(StringComparer.OrdinalIgnoreCase);

        void AddArchive(PodEntry? entry, string label, bool canBeDefault = true)
        {
            if (entry is null || !addedEntries.Add(entry.Name)) return;
            choices.Add(new PaletteChoice(label, TryDecodeAct(archive!.GetEntryBytes(entry))));
            if (canBeDefault && defaultIndex < 0) defaultIndex = choices.Count - 1;
        }

        if (archive is not null)
        {
            PodEntry? stored = FindStoredPalette(archive, rawNameField);
            AddArchive(stored, "Stored in the archive: " + stored?.Name);

            int dot = entryName.LastIndexOf('.');
            if (dot > 0)
            {
                PodEntry? sameName = archive.FindEntry(entryName.Substring(0, dot) + ".act");
                AddArchive(sameName, "Same-name ACT: " + sameName?.Name);
            }

            PodEntry? metal = FindArchivePalette(archive, MetalPalette);
            AddArchive(metal, "Archive " + (metal?.Name ?? MetalPalette));

            PodEntry? vga = FindArchivePalette(archive, VgaPalette);
            AddArchive(vga, "Archive " + (vga?.Name ?? VgaPalette));
        }

        int mtm1Index = choices.Count;
        choices.Add(new PaletteChoice("METALCR2 (MTM1)", BundledPalettes.MetalCr2Mtm1()));
        choices.Add(new PaletteChoice("METALCR2 (CPR)", BundledPalettes.MetalCr2Cpr()));
        choices.Add(new PaletteChoice("VGA (Hellbender)", BundledPalettes.VgaHellbender()));
        choices.Add(new PaletteChoice("VGA (TV/F3)", BundledPalettes.VgaTerminalVelocity()));
        choices.Add(new PaletteChoice("Greyscale", RawImageDecoder.GreyscalePalette()));
        if (defaultIndex < 0)
        {
            defaultIndex = mtm1Index;
        }

        // Every remaining palette in the archive, last and never the default. These are
        // the differently-named ones: real enough to be worth offering, and never a
        // safe guess, so they sit below the palettes that can be reasoned about.
        if (archive is not null)
        {
            foreach (PodEntry entry in archive.Entries)
            {
                if (entry.Name.EndsWith(".ACT", StringComparison.OrdinalIgnoreCase))
                    AddArchive(entry, "Archive ACT: " + entry.Name, canBeDefault: false);
            }
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
