using System;
using System.IO;
using System.Linq;

namespace HyperSend.Core
{
    public static class PathSanitizer
    {
        private static readonly string[] ReservedWindowsNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly char[] IllegalChars = Path.GetInvalidFileNameChars()
            .Where(c => c != '/' && c != '\\') // Allow forward and back slash in relative paths
            .ToArray();

        /// <summary>
        /// Validates and cleanses incoming relative path against path traversal attacks.
        /// Throws InvalidOperationException if path is malicious.
        /// </summary>
        public static string SanitizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path cannot be empty", nameof(relativePath));

            string trimmed = relativePath.Trim();
            if (trimmed.StartsWith('/') || trimmed.StartsWith('\\'))
                throw new InvalidOperationException($"Absolute or root paths are rejected: {relativePath}");

            // Normalize backslashes to forward slashes
            string normalized = trimmed.Replace('\\', '/');

            // Check for drive letter root (e.g. C:/) or UNC root (//server)
            if (normalized.Contains(':') || normalized.StartsWith("//"))
                throw new InvalidOperationException($"Absolute or UNC paths are rejected: {relativePath}");

            // Split into path segments and validate each segment
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new InvalidOperationException("Path contains no valid segments");

            foreach (var segment in segments)
            {
                if (segment == ".." || segment == ".")
                    throw new InvalidOperationException($"Directory traversal sequence detected: {relativePath}");

                if (segment.IndexOfAny(IllegalChars) >= 0)
                    throw new InvalidOperationException($"Path segment contains illegal characters: {segment}");

                // Check Windows reserved device names
                string baseName = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
                if (ReservedWindowsNames.Contains(baseName))
                    throw new InvalidOperationException($"Reserved Windows device name rejected: {segment}");
            }

            return string.Join('/', segments);
        }

        public static string ResolveSafeDestinationPath(string destinationDirectory, string relativePath)
        {
            string cleanRelative = SanitizeRelativePath(relativePath);
            string fullDestDir = Path.GetFullPath(destinationDirectory);
            string targetPath = Path.GetFullPath(Path.Combine(fullDestDir, cleanRelative.Replace('/', Path.DirectorySeparatorChar)));

            // Strict security assertion: targetPath MUST reside inside fullDestDir
            if (!targetPath.StartsWith(fullDestDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, fullDestDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Path traversal escape attempt detected: {relativePath}");
            }

            return targetPath;
        }
    }
}
