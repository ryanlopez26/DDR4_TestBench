using System;
using System.Collections.Generic;
using System.Text;

namespace DDR4_TestingApp
{
    internal static class Tools
    {
        public static string FormatBytes(uint bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int index = 0;

            while (size >= 1024 && index < suffixes.Length - 1)
            {
                size /= 1024;
                index++;
            }

            // Show decimals only when needed
            string formatted = (size % 1 == 0)
                ? $"{(uint)size} {suffixes[index]}"
                : $"{size:F1} {suffixes[index]}";

            return formatted;
        }

        public static string EpochToReadable(ulong epochSeconds)
        {
            DateTimeOffset dt = DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds);
            return dt.LocalDateTime.ToString("MM/dd/yy hh:mm:ss tt");
            //return dt.LocalDateTime.ToString("hh:mm:ss tt");
        }

        public static string FormatFixed(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "".PadRight(maxLength);
            return value.Length <= maxLength
                ? value.PadRight(maxLength)
                : value.Substring(0, maxLength);
        }

        public static string SelectFolder(string initialPath = "")
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder";
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;

                if (FileManager.IsValidWorkspace(initialPath))
                    dialog.InitialDirectory = initialPath;

                return dialog.ShowDialog() == DialogResult.OK
                    ? dialog.SelectedPath
                    : string.Empty;
            }
        }
    }
}
