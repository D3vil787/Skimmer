using System.Text;

namespace Skimmer;

static class AtomicFile
{
    /// <summary>Writes *.tmp first and then replaces the target, so a half-written file is never left behind.</summary>
    public static void WriteAllText(string path, string text, Encoding encoding)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, encoding);
        File.Move(tmp, path, overwrite: true);
    }
}
