using System;
using System.IO;

namespace StaticSiteGenerator
{
    public static class SafeFileCopy
    {
        public static void Copy(string sourcePath, string destinationPath, FileIgnoreList fileIgnoreList)
        {
            if (!fileIgnoreList.ShouldCopy(sourcePath))
            {
                return;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath);

            if (!Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            FileInfo sourceFileInfo = new FileInfo(sourcePath);
            FileInfo destinationFileInfo = new FileInfo(destinationPath);

            if (!destinationFileInfo.Exists ||
                destinationFileInfo.LastWriteTimeUtc < sourceFileInfo.LastWriteTimeUtc)
            {
                // Sometimes the file copy may fail because the browser is accessing the file - retry a few times
                int counter = 10;
                while (counter > 0)
                {
                    try
                    {
                        File.Copy(sourcePath, destinationPath.TrimEnd(), true);
                        break;
                    }
                    catch
                    {
                        counter--;
                    }
                }

                if (counter == 0)
                {
                    Console.WriteLine($"File copy failed for {destinationPath} - probably in-use.");
                }
            }

        }
    }
}
