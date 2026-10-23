#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;

namespace Emby.Server.Implementations.Library.Resolvers.Books
{
    public class BookResolver : ItemResolver<Book>
    {
        private readonly string[] _validExtensions = { ".azw", ".azw3", ".cb7", ".cbr", ".cbt", ".cbz", ".epub", ".mobi", ".pdf" };

        protected override Book Resolve(ItemResolveArgs args)
        {
            CollectionType? collectionType = args.GetCollectionType();

            // Only process items that are in a collection folder containing books
            if (collectionType != CollectionType.books)
            {
                return null;
            }

            if (args.IsDirectory)
            {
                return GetBook(args);
            }

            ReadOnlySpan<char> extension = Path.GetExtension(args.Path.AsSpan());

            if (_validExtensions.Contains(extension, StringComparison.OrdinalIgnoreCase))
            {
                // It's a book
                return new Book
                {
                    Path = args.Path,
                    IsInMixedFolder = true
                };
            }

            return null;
        }

        private Book GetBook(ItemResolveArgs args)
        {
            List<FileSystemMetadata> bookFiles = args.FileSystemChildren.Where(f =>
            {
                ReadOnlySpan<char> fileExtension = Path.GetExtension(f.FullName.AsSpan());

                return _validExtensions.Contains(
                    fileExtension,
                    StringComparison.OrdinalIgnoreCase);
            }).ToList();

            // Don't return a Book if there is more (or less) than one document in the directory
            if (bookFiles.Count != 1)
            {
                return null;
            }

            return new Book
            {
                Path = bookFiles[0].FullName
            };
        }
    }
}
