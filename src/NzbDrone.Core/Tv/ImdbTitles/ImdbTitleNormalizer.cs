using System.Globalization;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): the "does the series already have this title" key. NFD-decompose,
    // drop combining marks, lowercase, keep only [a-z0-9] - the same normalisation the external
    // series feeder (process_akas_series.py) used, so a dataset title differing from a stored
    // title only by accents, case, spacing or punctuation is treated as already present.
    public static class ImdbTitleNormalizer
    {
        public static string Normalize(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var decomposed = title.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                var lower = char.ToLowerInvariant(c);

                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
                {
                    builder.Append(lower);
                }
            }

            return builder.ToString();
        }
    }
}
