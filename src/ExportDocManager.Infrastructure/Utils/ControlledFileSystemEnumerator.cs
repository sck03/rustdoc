namespace ExportDocManager.Utils
{
    /// <summary>
    /// Enumerates a managed directory without ever following link-like file-system
    /// entries.  Directory.EnumerateFiles(..., AllDirectories) is intentionally not
    /// used here: on Windows a junction and on Unix a symlink can otherwise move a
    /// package scan outside its declared root.
    /// </summary>
    public static class ControlledFileSystemEnumerator
    {
        public static IReadOnlyList<string> EnumerateFiles(
            string root,
            CancellationToken cancellationToken = default,
            string errorMessage = "受管目录不能包含符号链接、目录联接或其他重解析点。")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);

            string fullRoot = Path.GetFullPath(root);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(fullRoot, errorMessage);
            if (!Directory.Exists(fullRoot))
            {
                if (File.Exists(fullRoot))
                {
                    throw new InvalidDataException($"受管路径不是目录：{fullRoot}");
                }

                // Directory.Exists returns false for a missing root.  Callers use an
                // absent optional template directory as an empty directory, while
                // inaccessible existing roots are surfaced by the attribute probe.
                try
                {
                    _ = File.GetAttributes(fullRoot);
                }
                catch (FileNotFoundException)
                {
                    return [];
                }
                catch (DirectoryNotFoundException)
                {
                    return [];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException(errorMessage, ex);
                }
            }

            EnsureSafePath(fullRoot, fullRoot, errorMessage);
            var files = new List<string>();
            WalkDirectory(fullRoot, fullRoot, files, cancellationToken, errorMessage, recurse: true);
            return SortFiles(files, fullRoot);
        }

        /// <summary>Enumerates only files directly below <paramref name="root"/>.</summary>
        public static IReadOnlyList<string> EnumerateImmediateFiles(
            string root,
            CancellationToken cancellationToken = default,
            string errorMessage = "受管目录不能包含符号链接、目录联接或其他重解析点。")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);

            string fullRoot = Path.GetFullPath(root);
            PathBoundaryHelper.EnsureNoLinkLikeComponents(fullRoot, errorMessage);
            if (!Directory.Exists(fullRoot))
            {
                if (File.Exists(fullRoot))
                {
                    throw new InvalidDataException($"受管路径不是目录：{fullRoot}");
                }

                // Directory.Exists intentionally returns false for an
                // inaccessible directory.  Probe the attributes so an access
                // failure cannot be mistaken for an optional empty root.
                try
                {
                    _ = File.GetAttributes(fullRoot);
                }
                catch (FileNotFoundException)
                {
                    return [];
                }
                catch (DirectoryNotFoundException)
                {
                    return [];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException(errorMessage, ex);
                }
            }

            EnsureSafePath(fullRoot, fullRoot, errorMessage);
            var files = new List<string>();
            WalkDirectory(fullRoot, fullRoot, files, cancellationToken, errorMessage, recurse: false);
            return SortFiles(files, fullRoot);
        }

        private static IReadOnlyList<string> SortFiles(List<string> files, string fullRoot)
        {
            files.Sort((left, right) =>
            {
                string leftRelative = PortablePathKey.NormalizeRelativePath(Path.GetRelativePath(fullRoot, left));
                string rightRelative = PortablePathKey.NormalizeRelativePath(Path.GetRelativePath(fullRoot, right));
                return PortablePathKey.Comparer.Equals(leftRelative, rightRelative)
                    ? PhysicalPathComparison.Comparer.Compare(left, right)
                    : PathBoundaryHelper.PathComparer.Compare(leftRelative, rightRelative);
            });
            return files;
        }

        private static void WalkDirectory(
            string directoryPath,
            string root,
            ICollection<string> files,
            CancellationToken cancellationToken,
            string errorMessage,
            bool recurse)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafePath(directoryPath, root, errorMessage);

            DirectoryInfo directory = new(directoryPath);
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos(
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = false,
                        IgnoreInaccessible = false,
                        ReturnSpecialDirectories = false,
                        AttributesToSkip = 0
                    });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(errorMessage, ex);
            }

            foreach (FileSystemInfo entry in entries.OrderBy(item => item.Name, PathBoundaryHelper.PathComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.GetFullPath(entry.FullName);
                EnsureSafePath(fullPath, root, errorMessage);

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"{errorMessage} 路径：{fullPath}");
                }

                if (entry is DirectoryInfo && recurse)
                {
                    WalkDirectory(fullPath, root, files, cancellationToken, errorMessage, recurse: true);
                }
                else if (entry is DirectoryInfo)
                {
                    // Immediate enumeration still validates child directories for
                    // reparse points; it simply does not descend into safe ones.
                }
                else if (entry is FileInfo)
                {
                    files.Add(fullPath);
                }
                else
                {
                    throw new InvalidDataException($"受管目录包含无法识别的文件系统条目：{fullPath}");
                }
            }
        }

        private static void EnsureSafePath(string path, string root, string errorMessage)
        {
            if (!PathBoundaryHelper.IsWithinRoot(path, root))
            {
                throw new InvalidDataException($"路径离开受管目录：{path}");
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"{errorMessage} 路径：{path}");
                }
            }
            catch (FileNotFoundException)
            {
                throw new InvalidDataException($"受管路径在扫描期间消失：{path}");
            }
            catch (DirectoryNotFoundException)
            {
                throw new InvalidDataException($"受管路径在扫描期间消失：{path}");
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (IOException ex)
            {
                throw new UnauthorizedAccessException(errorMessage, ex);
            }
        }
    }
}
