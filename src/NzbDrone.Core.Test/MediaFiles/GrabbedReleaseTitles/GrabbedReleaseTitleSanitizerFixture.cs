using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class GrabbedReleaseTitleSanitizerFixture : CoreTest
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n\t")]
        public void should_return_null_for_empty_input(string sourceTitle)
        {
            GrabbedReleaseTitleSanitizer.Sanitize(sourceTitle).Should().BeNull();
        }

        [Test]
        public void should_return_a_clean_title_unchanged()
        {
            const string title = "Series.Title.S01E01.MULTi.1080p.WEB.H264-GROUP";

            GrabbedReleaseTitleSanitizer.Sanitize(title).Should().Be(title);
        }

        [Test]
        public void should_keep_only_the_first_non_empty_line_of_a_tracker_description_blob()
        {
            const string blob = "White Chicks 2004 Unrated MULTi VF2 1080p WEBRip x264-PopHD (...)\n\t\n\t\n\t" +
                                "Taille: 4 GB Seeders: 27 Leechers: 4 Completes: 3079 Categories: Films";

            GrabbedReleaseTitleSanitizer.Sanitize(blob)
                                        .Should().Be("White Chicks 2004 Unrated MULTi VF2 1080p WEBRip x264-PopHD (...)");
        }

        [Test]
        public void should_handle_crlf_line_endings()
        {
            GrabbedReleaseTitleSanitizer.Sanitize("Series.Title.S01E01-GROUP\r\nTaille: 4 GB")
                                        .Should().Be("Series.Title.S01E01-GROUP");
        }

        [Test]
        public void should_skip_leading_blank_lines()
        {
            GrabbedReleaseTitleSanitizer.Sanitize("\r\n \t \r\nSeries.Title.S01E01-GROUP")
                                        .Should().Be("Series.Title.S01E01-GROUP");
        }

        [Test]
        public void should_collapse_internal_whitespace_and_trim()
        {
            GrabbedReleaseTitleSanitizer.Sanitize("  Series Title \t  S01E01   MULTi  \t")
                                        .Should().Be("Series Title S01E01 MULTi");
        }
    }
}
