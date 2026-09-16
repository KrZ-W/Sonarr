using System;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)
    // Some trackers put a whole description blob in the release title: the real title on the first
    // line, then LF/TAB padded "Taille: 4 GB Seeders: 27 ..." lines. Only the first line is a release
    // title, and custom format regexes are written against single-spaced titles, so keep the first
    // non-empty line, trim it and collapse internal whitespace runs to a single space.
    public static class GrabbedReleaseTitleSanitizer
    {
        private static readonly char[] LineSeparators = { '\r', '\n' };
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string Sanitize(string sourceTitle)
        {
            if (sourceTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var firstLine = sourceTitle.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault(line => line.IsNotNullOrWhiteSpace());

            if (firstLine == null)
            {
                return null;
            }

            var sanitized = WhitespaceRegex.Replace(firstLine, " ").Trim();

            return sanitized.IsNullOrWhiteSpace() ? null : sanitized;
        }
    }
}
