using System;
using System.Collections.Concurrent;
using System.IO;

namespace MainbotCSharp.Services
{
    public static class AtomicFileStore
    {
        private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);

        public static void WriteAllTextAtomic(string filePath, string content)
        {
            var fullPath = Path.GetFullPath(filePath);
            var lockObject = FileLocks.GetOrAdd(fullPath, _ => new object());

            lock (lockObject)
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                try
                {
                    File.WriteAllText(tempPath, content);

                    if (File.Exists(fullPath))
                    {
                        File.Replace(tempPath, fullPath, null, true);
                    }
                    else
                    {
                        File.Move(tempPath, fullPath);
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            }
        }
    }
}
