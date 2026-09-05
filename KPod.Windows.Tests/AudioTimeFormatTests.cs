using KPod.Windows.UI;

namespace KPod.Windows.Tests;

public class AudioTimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00.000")]
    [InlineData(62500, "1:02.500")]
    [InlineData(3723456, "1:02:03.456")]
    public void FormatsElapsedTime(long milliseconds, string expected)
    {
        Assert.Equal(expected, AudioPlayerForm.Format(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
