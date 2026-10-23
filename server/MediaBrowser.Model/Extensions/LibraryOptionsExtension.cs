using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Model.Extensions;

/// <summary>
/// Extensions for <see cref="LibraryOptions"/>.
/// </summary>
public static class LibraryOptionsExtension
{
    /// <summary>
    /// Get the custom tag delimiters.
    /// </summary>
    /// <param name="options">This LibraryOptions.</param>
    /// <returns>CustomTagDelimiters in char[].</returns>
    public static char[] GetCustomTagDelimiters(this LibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<char> delimiterList = options.CustomTagDelimiters.Select<string, char?>(x =>
        {
            bool isChar = char.TryParse(x, out char c);
            if (isChar)
            {
                return c;
            }

            return null;
        }).Where(x => x is not null).Select(x => x!.Value).ToList();
        delimiterList.Add('\0');
        return delimiterList.ToArray();
    }
}
