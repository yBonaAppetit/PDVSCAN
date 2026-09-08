using System.Drawing.Drawing2D;

namespace PdvBarcodeFilter;

internal sealed class StatusIconSet : IDisposable
{
    internal StatusIconSet()
    {
        Active = Create(Color.FromArgb(30, 170, 80));
        Paused = Create(Color.FromArgb(235, 165, 20));
        Error = Create(Color.FromArgb(210, 45, 45));
    }

    internal Icon Active { get; }
    internal Icon Paused { get; }
    internal Icon Error { get; }

    private static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            graphics.FillEllipse(fill, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 2);
            for (var x = 8; x <= 23; x += 3)
            {
                graphics.DrawLine(pen, x, 9, x, 23);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        Active.Dispose();
        Paused.Dispose();
        Error.Dispose();
    }
}
