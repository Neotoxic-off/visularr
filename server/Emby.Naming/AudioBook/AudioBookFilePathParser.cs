using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Emby.Naming.Common;

namespace Emby.Naming.AudioBook
{
    /// <summary>
    /// Parser class to extract part and/or chapter number from audiobook filename.
    /// </summary>
    public class AudioBookFilePathParser
    {
        private readonly NamingOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioBookFilePathParser"/> class.
        /// </summary>
        /// <param name="options">Naming options containing AudioBookPartsExpressions.</param>
        public AudioBookFilePathParser(NamingOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// Based on regex determines if filename includes part/chapter number.
        /// </summary>
        /// <param name="path">Path to audiobook file.</param>
        /// <returns>Returns <see cref="AudioBookFilePathParser"/> object.</returns>
        public AudioBookFilePathParserResult Parse(string path)
        {
            AudioBookFilePathParserResult result = default;
            string fileName = Path.GetFileNameWithoutExtension(path);
            foreach (string expression in _options.AudioBookPartsExpressions)
            {
                Match match = Regex.Match(fileName, expression, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    if (!result.ChapterNumber.HasValue)
                    {
                        Group value = match.Groups["chapter"];
                        if (value.Success)
                        {
                            if (int.TryParse(value.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                            {
                                result.ChapterNumber = intValue;
                            }
                        }
                    }

                    if (!result.PartNumber.HasValue)
                    {
                        Group value = match.Groups["part"];
                        if (value.Success)
                        {
                            if (int.TryParse(value.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                            {
                                result.PartNumber = intValue;
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
