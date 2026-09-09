using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): which rows of the akas dataset are worth keeping. A row is kept when
    // its region is one of the configured regions OR its language is one of the configured languages,
    // unless its attributes mark it as a literal translation (never a release name). Same rule as the
    // external feeder this feature replaces.
    public class ImdbAkasFilter
    {
        private const string LiteralAttribute = "literal";

        public ImdbAkasFilter(IEnumerable<string> regions, IEnumerable<string> languages)
        {
            Regions = new HashSet<string>(Clean(regions, upper: true), StringComparer.Ordinal);
            Languages = new HashSet<string>(Clean(languages, upper: false), StringComparer.Ordinal);
        }

        public HashSet<string> Regions { get; }
        public HashSet<string> Languages { get; }

        // The language attached to rows the dataset leaves language-less (typically region rows):
        // the first configured language.
        public string DefaultLanguage => Languages.FirstOrDefault();

        // Stable text form used to detect that the configured lists changed since the DB was built.
        public string Signature => $"regions={string.Join(",", Regions.OrderBy(r => r))};languages={string.Join(",", Languages.OrderBy(l => l))}";

        public static ImdbAkasFilter FromConfig(IConfigService configService)
        {
            return new ImdbAkasFilter(ParseList(configService.ImdbTitleProviderRegions), ParseList(configService.ImdbTitleProviderLanguages));
        }

        public static IEnumerable<string> ParseList(string commaSeparated)
        {
            return (commaSeparated ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.IsNotNullOrWhiteSpace());
        }

        public bool Keep(ImdbAkasRow row)
        {
            if (row == null || row.Title.IsNullOrWhiteSpace())
            {
                return false;
            }

            var matches = (row.Region != null && Regions.Contains(row.Region)) ||
                          (row.Language != null && Languages.Contains(row.Language));

            if (!matches)
            {
                return false;
            }

            return row.Attributes == null || !row.Attributes.Contains(LiteralAttribute, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Clean(IEnumerable<string> values, bool upper)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(v => v.IsNotNullOrWhiteSpace())
                .Select(v => upper ? v.Trim().ToUpperInvariant() : v.Trim().ToLowerInvariant());
        }
    }
}
