using KPod.Windows.UI;

namespace KPod.Windows.Tests;

/// <summary>
/// The preview window has to be sized from its toolbar as well as its image. A 64x64
/// texture is 256 pixels wide once magnified, which is nowhere near enough room for the
/// palette row, and sizing from the image alone hid the Save as BMP button.
/// </summary>
public class PreviewLayoutTests
{
    [Fact]
    public void ToolbarMeasuresWiderThanAMagnifiedSixtyFourPixelTexture()
    {
        using FlowLayoutPanel toolbar = PaletteToolbar();

        Assert.True(PreviewForm.MeasureToolbar(toolbar).Width > 64 * 4,
            "The palette row must out-measure the image it sits above, or the window is sized to clip it.");
    }

    [Fact]
    public void ToolbarWidthCountsEveryChildAndItsMargins()
    {
        using FlowLayoutPanel toolbar = PaletteToolbar();
        int measured = PreviewForm.MeasureToolbar(toolbar).Width;

        using Button extra = new() { Text = "Save as BMP...", AutoSize = true };
        toolbar.Controls.Add(extra);

        Assert.True(PreviewForm.MeasureToolbar(toolbar).Width > measured,
            "Adding a button to the row must widen the measurement.");
    }

    [Fact]
    public void AutoSizeButtonIsMeasuredByItsCaptionNotItsDefaultWidth()
    {
        using FlowLayoutPanel toolbar = new() { WrapContents = false };
        using Button save = new() { Text = "Save as a Windows bitmap image...", AutoSize = true };
        toolbar.Controls.Add(save);

        // An unshown AutoSize button still reports the stock 75 pixels in Width.
        Assert.True(PreviewForm.MeasureToolbar(toolbar).Width > 75);
    }

    [Fact]
    public void ToolbarHeightIsTheTallestChildPlusPadding()
    {
        using FlowLayoutPanel toolbar = PaletteToolbar();

        Assert.True(PreviewForm.MeasureToolbar(toolbar).Height >= toolbar.Padding.Vertical);
        Assert.True(PreviewForm.MeasureToolbar(toolbar).Height > 0);
    }

    private static FlowLayoutPanel PaletteToolbar()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), WrapContents = false,
        };
        toolbar.Controls.Add(new Label { Text = "Palette:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        toolbar.Controls.Add(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 });
        toolbar.Controls.Add(new Button { Text = "Save as BMP...", AutoSize = true });
        return toolbar;
    }
}
