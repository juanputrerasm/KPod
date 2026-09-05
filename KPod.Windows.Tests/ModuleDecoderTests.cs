using KPod.Windows.Platform;

namespace KPod.Windows.Tests;

public class ModuleDecoderTests
{
    [Fact]
    public void SixChannelModuleHasDurationAndNonSilentPcm()
    {
        using ModuleDecoder decoder = new(ModuleFixture.SixChannel());
        short[] pcm = new short[8192];

        int frames = decoder.Read(48000, pcm, 4096);

        Assert.True(decoder.Duration > TimeSpan.FromSeconds(1));
        Assert.True(frames > 0);
        Assert.Contains(pcm, sample => sample != 0);
    }

    [Fact]
    public void SeekingMovesAndCanReturnToStart()
    {
        using ModuleDecoder decoder = new(ModuleFixture.SixChannel());

        TimeSpan middle = decoder.Seek(TimeSpan.FromTicks(decoder.Duration.Ticks / 2));
        TimeSpan start = decoder.Seek(TimeSpan.Zero);

        Assert.InRange(middle.TotalSeconds, decoder.Duration.TotalSeconds * 0.35, decoder.Duration.TotalSeconds * 0.65);
        Assert.InRange(start.TotalMilliseconds, 0, 1);
    }

    [Fact]
    public void RejectsMalformedModule()
    {
        Assert.Throws<ArgumentException>(() => new ModuleDecoder([1, 2, 3, 4]));
    }

    [Fact]
    public void CanBeOpenedAndClosedRepeatedly()
    {
        byte[] data = ModuleFixture.SixChannel();
        for (int i = 0; i < 5; i++)
        {
            using ModuleDecoder decoder = new(data);
            Assert.True(decoder.Duration > TimeSpan.Zero);
        }
    }
}

internal static class ModuleFixture
{
    internal static byte[] SixChannel()
    {
        const int channels = 6;
        const int sampleBytes = 64;
        const int headerBytes = 1084;
        const int patternBytes = 64 * channels * 4;
        byte[] module = new byte[headerBytes + patternBytes + sampleBytes];
        WriteAscii(module, 0, "KPod six-channel test");
        module[42] = 0;
        module[43] = sampleBytes / 2;
        module[45] = 64;
        module[48] = 0;
        module[49] = 1;
        module[950] = 1;
        module[951] = 0;
        WriteAscii(module, 1080, "6CHN");

        int pattern = headerBytes;
        for (int channel = 0; channel < channels; channel++)
        {
            int note = pattern + (channel * 4);
            module[note] = 0x01;
            module[note + 1] = 0xAC;
            module[note + 2] = 0x10;
        }
        int sample = headerBytes + patternBytes;
        for (int i = 0; i < sampleBytes; i++)
            module[sample + i] = unchecked((byte)(sbyte)(Math.Sin(i * Math.PI * 2 / sampleBytes) * 110));
        return module;
    }

    private static void WriteAscii(byte[] destination, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++) destination[offset + i] = (byte)text[i];
    }
}
