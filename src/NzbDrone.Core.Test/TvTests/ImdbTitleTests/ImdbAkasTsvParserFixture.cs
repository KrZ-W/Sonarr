using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv.ImdbTitles;

namespace NzbDrone.Core.Test.TvTests.ImdbTitleTests
{
    [TestFixture]
    public class ImdbAkasTsvParserFixture : CoreTest
    {
        // A slice of title.akas.tsv: header + rows in the real column layout.
        public const string Sample =
            "titleId\tordering\ttitle\tregion\tlanguage\ttypes\tattributes\tisOriginalTitle\n" +
            "tt0211915\t1\tAmélie\t\\N\t\\N\toriginal\t\\N\t1\n" +
            "tt0211915\t2\tLe Fabuleux Destin d'Amélie Poulain\tFR\t\\N\timdbDisplay\t\\N\t0\n" +
            "tt0211915\t3\tAmélie de Montmartre\tCA\tfr\timdbDisplay\t\\N\t0\n" +
            "tt0211915\t4\tAmelie\tUS\t\\N\timdbDisplay\t\\N\t0\n" +
            "tt0211915\t5\tAmélie from Montmartre\tCA\ten\t\\N\tliteral English title\t0\n" +
            "tt0211915\t6\tLe destin fabuleux\tBE\tfr\t\\N\t\\N\t0\n" +
            "tt0211915\t7\tDestin\tDE\t\\N\t\\N\tLiteral title\t0\n" +
            "tt0068646\t1\tLe Parrain\tCA\t\\N\timdbDisplay\t\\N\t0\n" +
            "broken line without enough columns\n" +
            "tt0068646\t2\tThe Godfather\tGB\t\\N\timdbDisplay\t\\N\t0\n";

        public static byte[] Gzip(string text)
        {
            using var output = new MemoryStream();

            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                gzip.Write(bytes, 0, bytes.Length);
            }

            return output.ToArray();
        }

        private static ImdbAkasFilter DefaultFilter()
        {
            return new ImdbAkasFilter(new[] { "CA", "FR" }, new[] { "fr" });
        }

        [Test]
        public void should_keep_rows_by_region_or_language_and_skip_literal()
        {
            var stats = new ImdbAkasParseStats();
            var rows = ImdbAkasTsvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Sample)), DefaultFilter(), stats).ToList();

            rows.Select(r => r.Title).Should().Equal(
                "Le Fabuleux Destin d'Amélie Poulain",
                "Amélie de Montmartre",
                "Le destin fabuleux",
                "Le Parrain");

            stats.Scanned.Should().Be(10);
            stats.Kept.Should().Be(4);
        }

        [Test]
        public void should_map_null_columns_and_original_flag()
        {
            var rows = ImdbAkasTsvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Sample)), new ImdbAkasFilter(new[] { "CA", "FR", "US" }, System.Array.Empty<string>())).ToList();

            var fr = rows.Single(r => r.Region == "FR");
            fr.Tconst.Should().Be("tt0211915");
            fr.Language.Should().BeNull();
            fr.Attributes.Should().BeNull();
            fr.IsOriginal.Should().BeFalse();

            var ca = rows.Single(r => r.Title == "Amélie de Montmartre");
            ca.Region.Should().Be("CA");
            ca.Language.Should().Be("fr");
        }

        [Test]
        public void should_treat_region_and_language_codes_case_insensitively_in_configuration()
        {
            var filter = new ImdbAkasFilter(new[] { " ca ", "fr" }, new[] { "FR" });

            filter.Regions.Should().BeEquivalentTo("CA", "FR");
            filter.Languages.Should().BeEquivalentTo("fr");
            filter.DefaultLanguage.Should().Be("fr");
            filter.Signature.Should().Be("regions=CA,FR;languages=fr");
        }

        [Test]
        public void should_keep_language_rows_from_any_region()
        {
            var filter = new ImdbAkasFilter(System.Array.Empty<string>(), new[] { "fr" });
            var rows = ImdbAkasTsvParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Sample)), filter).ToList();

            rows.Select(r => r.Title).Should().Equal("Amélie de Montmartre", "Le destin fabuleux");
        }

        [Test]
        public void should_parse_gzip_file()
        {
            var path = GetTempFilePath();
            File.WriteAllBytes(path, Gzip(Sample));

            var rows = ImdbAkasTsvParser.ParseGzipFile(path, DefaultFilter()).ToList();

            rows.Should().HaveCount(4);
        }

        [Test]
        public void should_return_nothing_for_empty_input()
        {
            ImdbAkasTsvParser.Parse(new MemoryStream(), DefaultFilter()).Should().BeEmpty();
        }

        [TestCase("Amélie de Montmartre", "ameliedemontmartre")]
        [TestCase("  (500) Jours avec SUMMER!  ", "500joursavecsummer")]
        [TestCase("L'Étrange Noël de M. Jack", "letrangenoeldemjack")]
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase("!!!", "")]
        public void should_normalize_titles_like_the_reference_feeder(string title, string expected)
        {
            ImdbTitleNormalizer.Normalize(title).Should().Be(expected);
        }
    }
}
