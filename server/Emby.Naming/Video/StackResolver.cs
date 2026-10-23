using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emby.Naming.AudioBook;
using Emby.Naming.Common;
using MediaBrowser.Model.IO;

namespace Emby.Naming.Video
{
    /// <summary>
    /// Resolve <see cref="FileStack"/> from list of paths.
    /// </summary>
    public static class StackResolver
    {
        /// <summary>
        /// Resolves only directories from paths.
        /// </summary>
        /// <param name="files">List of paths.</param>
        /// <param name="namingOptions">The naming options.</param>
        /// <returns>Enumerable <see cref="FileStack"/> of directories.</returns>
        public static IEnumerable<FileStack> ResolveDirectories(IEnumerable<string> files, NamingOptions namingOptions)
        {
            return Resolve(files.Select(i => new FileSystemMetadata { FullName = i, IsDirectory = true }), namingOptions);
        }

        /// <summary>
        /// Resolves only files from paths.
        /// </summary>
        /// <param name="files">List of paths.</param>
        /// <param name="namingOptions">The naming options.</param>
        /// <returns>Enumerable <see cref="FileStack"/> of files.</returns>
        public static IEnumerable<FileStack> ResolveFiles(IEnumerable<string> files, NamingOptions namingOptions)
        {
            return Resolve(files.Select(i => new FileSystemMetadata { FullName = i, IsDirectory = false }), namingOptions);
        }

        /// <summary>
        /// Resolves audiobooks from paths.
        /// </summary>
        /// <param name="files">List of paths.</param>
        /// <returns>Enumerable <see cref="FileStack"/> of directories.</returns>
        public static IEnumerable<FileStack> ResolveAudioBooks(IEnumerable<AudioBookFileInfo> files)
        {
            IEnumerable<IGrouping<string?, AudioBookFileInfo>> groupedDirectoryFiles = files.GroupBy(file => Path.GetDirectoryName(file.Path));

            foreach (IGrouping<string?, AudioBookFileInfo> directory in groupedDirectoryFiles)
            {
                if (string.IsNullOrEmpty(directory.Key))
                {
                    foreach (AudioBookFileInfo? file in directory)
                    {
                        FileStack stack = new FileStack(Path.GetFileNameWithoutExtension(file.Path), false, new[] { file.Path });
                        yield return stack;
                    }
                }
                else
                {
                    FileStack stack = new FileStack(Path.GetFileName(directory.Key), false, directory.Select(f => f.Path).ToArray());
                    yield return stack;
                }
            }
        }

        /// <summary>
        /// Resolves videos from paths.
        /// </summary>
        /// <param name="files">List of paths.</param>
        /// <param name="namingOptions">The naming options.</param>
        /// <returns>Enumerable <see cref="FileStack"/> of videos.</returns>
        public static IEnumerable<FileStack> Resolve(IEnumerable<FileSystemMetadata> files, NamingOptions namingOptions)
        {
            IOrderedEnumerable<FileSystemMetadata> potentialFiles = files
                .Where(i => i.IsDirectory || VideoResolver.IsVideoFile(i.FullName, namingOptions) || VideoResolver.IsStubFile(i.FullName, namingOptions))
                .OrderBy(i => i.FullName);

            Dictionary<string, StackMetadata> potentialStacks = new Dictionary<string, StackMetadata>();
            foreach (FileSystemMetadata? file in potentialFiles)
            {
                string name = file.Name;
                if (string.IsNullOrEmpty(name))
                {
                    name = Path.GetFileName(file.FullName);
                }

                for (int i = 0; i < namingOptions.VideoFileStackingRules.Length; i++)
                {
                    FileStackRule rule = namingOptions.VideoFileStackingRules[i];
                    if (!rule.Match(name, out (string StackName, string PartType, string PartNumber)? stackParsingResult))
                    {
                        continue;
                    }

                    string stackName = stackParsingResult.Value.StackName;
                    string partNumber = stackParsingResult.Value.PartNumber;
                    string partType = stackParsingResult.Value.PartType;

                    if (!potentialStacks.TryGetValue(stackName, out StackMetadata? stackResult))
                    {
                        stackResult = new StackMetadata(file.IsDirectory, rule.IsNumerical, partType);
                        potentialStacks[stackName] = stackResult;
                    }

                    if (stackResult.Parts.Count > 0)
                    {
                        if (stackResult.IsDirectory != file.IsDirectory
                            || !string.Equals(partType, stackResult.PartType, StringComparison.OrdinalIgnoreCase)
                            || stackResult.ContainsPart(partNumber))
                        {
                            continue;
                        }

                        if (rule.IsNumerical != stackResult.IsNumerical)
                        {
                            break;
                        }
                    }

                    stackResult.Parts.Add(partNumber, file);
                    break;
                }
            }

            foreach ((string? fileName, StackMetadata? stack) in potentialStacks)
            {
                if (stack.Parts.Count < 2)
                {
                    continue;
                }

                yield return new FileStack(fileName, stack.IsDirectory, stack.Parts.Select(kv => kv.Value.FullName).ToArray());
            }
        }

        private sealed class StackMetadata
        {
            public StackMetadata(bool isDirectory, bool isNumerical, string partType)
            {
                Parts = new Dictionary<string, FileSystemMetadata>(StringComparer.OrdinalIgnoreCase);
                IsDirectory = isDirectory;
                IsNumerical = isNumerical;
                PartType = partType;
            }

            public Dictionary<string, FileSystemMetadata> Parts { get; }

            public bool IsDirectory { get; }

            public bool IsNumerical { get; }

            public string PartType { get; }

            public bool ContainsPart(string partNumber) => Parts.ContainsKey(partNumber);
        }
    }
}
