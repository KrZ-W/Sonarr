using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): streams title.akas.tsv(.gz) line by line and yields the rows the
    // filter keeps. Nothing but the current line is ever held in memory: the full dump is ~2 GB
    // uncompressed and tens of millions of rows.
    public static class ImdbAkasTsvParser
    {
        private const string Null = "\\N";

        public static IEnumerable<ImdbAkasRow> ParseGzipFile(string path, ImdbAkasFilter filter, ImdbAkasParseStats stats = null)
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);

            foreach (var row in Parse(gzip, filter, stats))
            {
                yield return row;
            }
        }

        public static IEnumerable<ImdbAkasRow> Parse(Stream tsv, ImdbAkasFilter filter, ImdbAkasParseStats stats = null)
        {
            using var reader = new StreamReader(tsv, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);

            var header = reader.ReadLine();

            if (header == null)
            {
                yield break;
            }

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (stats != null)
                {
                    stats.Scanned++;
                }

                var row = ParseLine(line);

                if (row == null || !filter.Keep(row))
                {
                    continue;
                }

                if (stats != null)
                {
                    stats.Kept++;
                }

                yield return row;
            }
        }

        // titleId ordering title region language types attributes isOriginalTitle
        public static ImdbAkasRow ParseLine(string line)
        {
            var parts = line.Split('\t');

            if (parts.Length < 8)
            {
                return null;
            }

            return new ImdbAkasRow
            {
                Tconst = parts[0],
                Title = Value(parts[2]),
                Region = Value(parts[3]),
                Language = Value(parts[4]),
                Attributes = Value(parts[6]),
                IsOriginal = parts[7] == "1"
            };
        }

        private static string Value(string field)
        {
            return field == Null || field.Length == 0 ? null : field;
        }
    }

    public class ImdbAkasParseStats
    {
        public long Scanned { get; set; }
        public long Kept { get; set; }
    }
}
