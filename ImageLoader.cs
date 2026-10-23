using System.Drawing;
using System.Drawing.Imaging;
using Wm = System.Windows.Media;
using Wmi = System.Windows.Media.Imaging;

namespace Skimmer;

sealed class LoadedImage
{
    public Bitmap Bmp;
    public int OrigW, OrigH;   // real picture size (Bmp may be smaller when decoded to fit the screen)
}

/// <summary>
/// Image decoding through WPF/WIC: JPEGs are decoded with DCT scaling, which makes
/// thumbnails and screen-sized previews several times faster than plain GDI+.
/// </summary>
static class ImageLoader
{
    public const int ThumbPx = 256;
    const int ThumbQuality = 80;

    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff",
        ".webp", ".heic", ".heif", ".jxr", ".wdp", ".ico"
    };

    public static bool IsImage(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        return ext.Length > 0 && Extensions.Contains(ext);
    }

    static FileStream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);

    static int ReadOrientation(Wmi.BitmapFrame frame, string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string query = null;
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".jpe" || ext == ".jfif") query = "/app1/ifd/{ushort=274}";
            else if (ext == ".tif" || ext == ".tiff") query = "/ifd/{ushort=274}";
            if (query != null && frame.Metadata is Wmi.BitmapMetadata md && md.GetQuery(query) is ushort o)
                return o;
        }
        catch { }
        return 1;
    }

    static Wmi.BitmapSource Rotate(Wmi.BitmapSource s, double angle)
    {
        var t = new Wmi.TransformedBitmap(s, new Wm.RotateTransform(angle));
        t.Freeze();
        return t;
    }

    static Wmi.BitmapSource Flip(Wmi.BitmapSource s, bool horizontal)
    {
        var t = new Wmi.TransformedBitmap(s, horizontal ? new Wm.ScaleTransform(-1, 1) : new Wm.ScaleTransform(1, -1));
        t.Freeze();
        return t;
    }

    static Wmi.BitmapSource ApplyOrientation(Wmi.BitmapSource s, int o) => o switch
    {
        2 => Flip(s, true),
        3 => Rotate(s, 180),
        4 => Flip(s, false),
        5 => Flip(Rotate(s, 90), true),
        6 => Rotate(s, 90),
        7 => Flip(Rotate(s, 90), false),
        8 => Rotate(s, 270),
        _ => s
    };

    /// <summary>Decodes to fit inside targetW x targetH (0 = native size), EXIF orientation applied.</summary>
    static Wmi.BitmapSource Decode(string path, int targetW, int targetH, out int dispW, out int dispH)
    {
        using var fs = OpenRead(path);
        var frame = Wmi.BitmapFrame.Create(fs,
            Wmi.BitmapCreateOptions.DelayCreation | Wmi.BitmapCreateOptions.IgnoreColorProfile,
            Wmi.BitmapCacheOption.None);
        int w = frame.PixelWidth, h = frame.PixelHeight;
        int orient = ReadOrientation(frame, path);
        bool swap = orient >= 5;
        dispW = swap ? h : w;
        dispH = swap ? w : h;

        fs.Position = 0;
        var bi = new Wmi.BitmapImage();
        bi.BeginInit();
        bi.CacheOption = Wmi.BitmapCacheOption.OnLoad;
        bi.CreateOptions = Wmi.BitmapCreateOptions.IgnoreColorProfile;
        bi.StreamSource = fs;
        if (targetW > 0 && targetH > 0)
        {
            double scale = Math.Min((double)targetW / dispW, (double)targetH / dispH);
            if (scale < 1)
            {
                if (w >= h) bi.DecodePixelWidth = Math.Max(1, (int)Math.Round(w * scale));
                else bi.DecodePixelHeight = Math.Max(1, (int)Math.Round(h * scale));
            }
        }
        bi.EndInit();
        bi.Freeze();

        Wmi.BitmapSource src = bi;
        if (orient > 1) src = ApplyOrientation(src, orient);
        return src;
    }

    /// <summary>JPEG bytes of a thumbnail with the longest side of ThumbPx.</summary>
    public static byte[] CreateThumbJpeg(string path)
    {
        var src = Decode(path, ThumbPx, ThumbPx, out _, out _);
        var conv = new Wmi.FormatConvertedBitmap(src, Wm.PixelFormats.Bgr24, null, 0);
        var enc = new Wmi.JpegBitmapEncoder { QualityLevel = ThumbQuality };
        enc.Frames.Add(Wmi.BitmapFrame.Create(conv));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Full image for the viewer. maxW/maxH = 0 means native resolution.</summary>
    public static LoadedImage LoadFull(string path, int maxW, int maxH)
    {
        var src = Decode(path, maxW, maxH, out int ow, out int oh);
        var conv = new Wmi.FormatConvertedBitmap(src, Wm.PixelFormats.Pbgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            conv.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * h, data.Stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return new LoadedImage { Bmp = bmp, OrigW = ow, OrigH = oh };
    }

    /// <summary>Decodes a cached thumbnail into a fast-to-draw GDI+ bitmap.</summary>
    public static Bitmap BitmapFromJpeg(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var img = Image.FromStream(ms, false, false);
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, 0, 0, img.Width, img.Height);
        return bmp;
    }
}
