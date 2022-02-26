using System.IO;

namespace StaticSiteGenerator
{
    public static class SafeFileWriter
    {
        public static void Write(string sourcePath, string content)
        {
            FileInfo existingFileInfo = new FileInfo(sourcePath);
            if (existingFileInfo.Exists && existingFileInfo.IsReadOnly)
            {
                existingFileInfo.IsReadOnly = false;
            }

            File.WriteAllText(sourcePath, content);
        }
    }
}
