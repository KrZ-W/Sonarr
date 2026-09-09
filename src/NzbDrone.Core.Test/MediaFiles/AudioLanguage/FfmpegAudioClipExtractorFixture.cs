using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class FfmpegAudioClipExtractorFixture : CoreTest
    {
        [Test]
        public void should_build_a_plain_argument_list_for_one_mono_pcm_clip()
        {
            var args = FfmpegAudioClipExtractor.BuildArguments("/downloads/Movie (2020).mkv", 1, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), "/tmp/clip.wav");

            args.Should().Be("-y -nostdin -loglevel error -ss 300 -t 30 -i \"/downloads/Movie (2020).mkv\" -map 0:a:1 -vn -ac 1 -ar 16000 -c:a pcm_s16le \"/tmp/clip.wav\"");
        }

        [Test]
        public void should_escape_quotes_in_paths()
        {
            var args = FfmpegAudioClipExtractor.BuildArguments("/downloads/a\"b.mkv", 0, TimeSpan.Zero, TimeSpan.FromSeconds(30), "/tmp/clip.wav");

            args.Should().Contain("-i \"/downloads/a\\\"b.mkv\"");
        }

        [Test]
        public void should_use_invariant_culture_and_clamp_length()
        {
            var args = FfmpegAudioClipExtractor.BuildArguments("/a.mkv", 0, TimeSpan.FromSeconds(12.5), TimeSpan.Zero, "/tmp/clip.wav");

            args.Should().Contain("-ss 12.5 -t 1 ");
        }

        [Test]
        public void should_resolve_a_binary_name_or_bundled_path()
        {
            FfmpegAudioClipExtractor.ResolveBinary().Should().MatchRegex("ffmpeg(\\.exe)?$");
        }
    }
}
