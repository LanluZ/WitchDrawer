namespace WitchDrawer.App.Infrastructure;

public static class DpiAwareIconSize
{
    private const double PixelatedSourceSizeDip = 16;
    private static readonly int[] SourcePixelBuckets = [16, 20, 24, 32, 40, 48, 64, 96, 128];

    public static int Calculate(
        double displayWidthDip,
        double displayHeightDip,
        double dpiScaleX,
        double dpiScaleY,
        bool isPixelated)
    {
        var widthDip = NormalizePositive(displayWidthDip, 16);
        var heightDip = NormalizePositive(displayHeightDip, widthDip);
        var scaleX = NormalizePositive(dpiScaleX, 1);
        var scaleY = NormalizePositive(dpiScaleY, 1);

        var targetWidthDip = isPixelated
            ? Math.Min(widthDip, PixelatedSourceSizeDip)
            : widthDip;
        var targetHeightDip = isPixelated
            ? Math.Min(heightDip, PixelatedSourceSizeDip)
            : heightDip;
        var targetPixels = (int)Math.Ceiling(Math.Max(targetWidthDip * scaleX, targetHeightDip * scaleY));

        foreach (var bucket in SourcePixelBuckets)
        {
            if (targetPixels <= bucket)
            {
                return bucket;
            }
        }

        return SourcePixelBuckets[^1];
    }

    private static double NormalizePositive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }
}
