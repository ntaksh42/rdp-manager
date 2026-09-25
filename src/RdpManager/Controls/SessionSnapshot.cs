using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using PixelFormats = System.Windows.Media.PixelFormats;
using Rectangle = System.Drawing.Rectangle;

namespace RdpManager.Controls;

/// <summary>セッション一覧用スナップショットの GDI+ Bitmap → WPF 変換と空画像判定。</summary>
internal static class SessionSnapshot
{
    /// <summary>ほぼ真っ黒（全画素の各チャネルが閾値未満）なら true。非表示中の取得失敗の検出に使う。
    /// 実画面なら大抵先頭付近で非黒画素に当たるため早期終了する。</summary>
    public static bool IsBlank(Bitmap bmp)
    {
        const int threshold = 8;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            int width = data.Width;
            var row = new int[width];
            for (int y = 0; y < data.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, width);
                foreach (int px in row)
                {
                    if ((px & 0xFF) >= threshold || ((px >> 8) & 0xFF) >= threshold || ((px >> 16) & 0xFF) >= threshold)
                        return false;
                }
            }
            return true;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>幅が maxWidth を超えていれば縮小し、凍結済みの BitmapSource にする（メモリ使用量を抑えるため）。</summary>
    public static BitmapSource ToBitmapSource(Bitmap source, int maxWidth)
    {
        Bitmap? scaled = null;
        try
        {
            var bmp = source;
            if (source.Width > maxWidth)
            {
                int h = Math.Max(1, (int)Math.Round(source.Height * (double)maxWidth / source.Width));
                scaled = new Bitmap(maxWidth, h, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, 0, 0, maxWidth, h);
                }
                bmp = scaled;
            }

            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                var result = BitmapSource.Create(data.Width, data.Height, 96, 96, PixelFormats.Bgr32, null,
                    data.Scan0, data.Stride * data.Height, data.Stride);
                result.Freeze();
                return result;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
