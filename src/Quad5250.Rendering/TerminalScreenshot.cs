using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quad5250.Protocol.Screen;

namespace Quad5250.Rendering;

public sealed record ScreenshotOptions
{
    public TerminalTheme Theme { get; init; } = TerminalTheme.Green;
    public double FontSize { get; init; } = 18;
    /// <summary>Pixel density multiplier; 2 gives a crisp image on high-DPI displays and in documents.</summary>
    public double Scale { get; init; } = 2;
    /// <summary>Margin in device-independent pixels, painted in the theme background.</summary>
    public double Padding { get; init; } = 12;
    /// <summary>Off by default: a cursor block in a documentation image is usually noise.</summary>
    public bool ShowCursor { get; init; }
    public IReadOnlyCollection<CellRange> Redactions { get; init; } = [];
}

/// <summary>
/// Renders a screen to an image without a window, using the same
/// <see cref="TerminalRenderer"/> as the desktop app. Safe to call from any
/// thread: WPF rendering needs STA, so work is moved onto one when needed.
/// </summary>
public static class TerminalScreenshot
{
    public static BitmapSource RenderBitmap(ScreenSnapshot screen, ScreenshotOptions? options = null)
        => OnSta(() => Render(screen, options ?? new ScreenshotOptions()));

    public static byte[] RenderPng(ScreenSnapshot screen, ScreenshotOptions? options = null)
        => OnSta(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(screen, options ?? new ScreenshotOptions())));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        });

    private static BitmapSource Render(ScreenSnapshot screen, ScreenshotOptions o)
    {
        var renderer = new TerminalRenderer(o.FontSize);
        var grid = renderer.GridSize(screen.Rows, screen.Cols);
        var width = grid.Width + o.Padding * 2;
        var height = grid.Height + o.Padding * 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = new SolidColorBrush(o.Theme.Background);
            bg.Freeze();
            dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));
            dc.PushTransform(new TranslateTransform(o.Padding, o.Padding));
            renderer.Render(dc, screen, o.Theme, o.Scale, o.ShowCursor, o.Redactions);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * o.Scale), (int)Math.Ceiling(height * o.Scale),
            96 * o.Scale, 96 * o.Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static T OnSta<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return work();

        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Screenshot rendering failed.", error);
        return result!;
    }
}
