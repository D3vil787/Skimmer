using System.Globalization;
using System.Runtime.InteropServices;

namespace Skimmer;

static class Native
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string a, string b);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

/// <summary>Explorer-style natural ordering: img2.jpg before img10.jpg.</summary>
sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();
    public int Compare(string a, string b) => Native.StrCmpLogicalW(a, b);
}

static class Fmt
{
    public static string Size(long bytes)
    {
        double b = bytes;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i == 0
            ? $"{bytes} {u[0]}"
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", b, u[i]);
    }

    public static string Date(DateTime dt) => dt.ToString("g", CultureInfo.CurrentCulture);
}
