using System;
using System.IO;
using System.Text;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>
    /// Crash-safe file helpers: write to a temp file then atomically replace the
    /// target, and quarantine unreadable files instead of silently overwriting them.
    /// </summary>
    internal static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                fs.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>
        /// Rename an unreadable file to <c>path.corrupt-yyyyMMddHHmmss</c> so the next
        /// save doesn't overwrite it. Returns the new path, or null if nothing was moved.
        /// </summary>
        public static string? QuarantineCorrupt(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var target = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(path, target, overwrite: true);
                return target;
            }
            catch
            {
                return null;
            }
        }
    }
}
