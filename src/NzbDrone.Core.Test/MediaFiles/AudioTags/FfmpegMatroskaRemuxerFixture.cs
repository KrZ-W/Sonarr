using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles.AudioTags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioTags
{
    [TestFixture]
    public class FfmpegMatroskaRemuxerFixture : CoreTest<FfmpegMatroskaRemuxer>
    {
        private const string Source = "/movies/Movie (2020)/Movie (2020).mp4";
        private const string Target = "/movies/Movie (2020)/Movie (2020).krzw-remux.tmp.mkv";

        // typical MP4: video, two audio tracks, a tx3g subtitle and a timecode data stream
        private const string Mp4Streams = @"{ ""streams"": [
            { ""index"": 0, ""codec_type"": ""video"", ""codec_name"": ""h264"" },
            { ""index"": 1, ""codec_type"": ""audio"", ""codec_name"": ""aac"" },
            { ""index"": 2, ""codec_type"": ""audio"", ""codec_name"": ""ac3"" },
            { ""index"": 3, ""codec_type"": ""subtitle"", ""codec_name"": ""mov_text"" },
            { ""index"": 4, ""codec_type"": ""data"", ""codec_name"": ""tmcd"" } ] }";

        private string _binary;
        private string _args;
        private Action<string> _onError;
        private Process _spawned;

        [SetUp]
        public void Setup()
        {
            _binary = null;
            _args = null;
            _onError = null;
            _spawned = null;

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(false);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_spawned != null && !_spawned.HasExited)
                {
                    _spawned.Kill();
                }
            }
            catch
            {
            }
        }

        private void GivenProcess(string command, string arguments = null, params string[] stderrLines)
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) =>
                  {
                      _binary = path;
                      _args = args;
                      _onError = e;

                      foreach (var line in stderrLines)
                      {
                          e(line);
                      }
                  })
                  .Returns<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) =>
                      _spawned = Process.Start(new ProcessStartInfo(command, arguments ?? string.Empty) { UseShellExecute = false }));
        }

        private static List<AudioTrackRetagTrack> Edits(params (int Index, string To)[] edits)
        {
            var list = new List<AudioTrackRetagTrack>();

            foreach (var (index, to) in edits)
            {
                list.Add(new AudioTrackRetagTrack { StreamIndex = index, From = "eng", To = to });
            }

            return list;
        }

        // ----- stream map -----

        [Test]
        public void should_keep_video_audio_and_carriable_subtitles_and_drop_the_rest()
        {
            var map = FfmpegMatroskaRemuxer.BuildStreamMap(Mp4Streams);

            map.MapAll.Should().BeFalse();
            map.Kept.Should().Equal(0, 1, 2);
            map.Dropped.Should().Equal("#3 subtitle mov_text", "#4 data tmcd");
        }

        [Test]
        public void should_keep_matroska_subtitles_and_attachments()
        {
            var map = FfmpegMatroskaRemuxer.BuildStreamMap(@"{ ""streams"": [
                { ""index"": 0, ""codec_type"": ""video"", ""codec_name"": ""hevc"" },
                { ""index"": 1, ""codec_type"": ""audio"", ""codec_name"": ""eac3"" },
                { ""index"": 2, ""codec_type"": ""subtitle"", ""codec_name"": ""subrip"" },
                { ""index"": 3, ""codec_type"": ""subtitle"", ""codec_name"": ""hdmv_pgs_subtitle"" },
                { ""index"": 4, ""codec_type"": ""attachment"", ""codec_name"": ""ttf"" } ] }");

            map.Kept.Should().Equal(0, 1, 2, 3, 4);
            map.Dropped.Should().BeEmpty();
        }

        [Test]
        public void should_never_drop_an_audio_or_video_stream_because_of_its_codec()
        {
            // an audio codec Matroska may reject is ffmpeg's call (the remux then fails), never silently dropped
            var map = FfmpegMatroskaRemuxer.BuildStreamMap(@"{ ""streams"": [
                { ""index"": 0, ""codec_type"": ""video"", ""codec_name"": ""mpeg4"" },
                { ""index"": 1, ""codec_type"": ""audio"", ""codec_name"": ""adpcm_ima_wav"" } ] }");

            map.Kept.Should().Equal(0, 1);
            map.Dropped.Should().BeEmpty();
        }

        [Test]
        public void should_map_everything_when_the_layout_is_unknown()
        {
            FfmpegMatroskaRemuxer.BuildStreamMap(null).MapAll.Should().BeTrue();
            FfmpegMatroskaRemuxer.BuildStreamMap("not json").MapAll.Should().BeTrue();
            FfmpegMatroskaRemuxer.BuildStreamMap(@"{ ""format"": {} }").MapAll.Should().BeTrue();
            FfmpegMatroskaRemuxer.BuildStreamMap(@"{ ""streams"": [ { ""index"": 0, ""codec_type"": ""data"", ""codec_name"": ""bin_data"" } ] }").MapAll.Should().BeTrue();
        }

        [Test]
        public void should_count_streams()
        {
            FfmpegMatroskaRemuxer.CountStreams(Mp4Streams).Should().Be(5);
            FfmpegMatroskaRemuxer.CountStreams(null).Should().Be(-1);
            FfmpegMatroskaRemuxer.CountStreams("{}").Should().Be(-1);
        }

        // ----- arguments -----

        [Test]
        public void should_stream_copy_the_kept_streams_and_set_the_language_of_each_edited_audio_track()
        {
            var map = FfmpegMatroskaRemuxer.BuildStreamMap(Mp4Streams);

            var args = FfmpegMatroskaRemuxer.BuildArguments(Source, Target, map, Edits((1, "fre"), (0, "ger")));

            args.Should().Be("-y -nostdin -loglevel warning -fflags +genpts -i \"/movies/Movie (2020)/Movie (2020).mp4\" -map 0:0 -map 0:1 -map 0:2 -c copy -metadata:s:a:0 language=ger -metadata:s:a:1 language=fre -f matroska \"/movies/Movie (2020)/Movie (2020).krzw-remux.tmp.mkv\"");
        }

        [Test]
        public void should_never_contain_an_encoder()
        {
            var args = FfmpegMatroskaRemuxer.BuildArguments(Source, Target, FfmpegMatroskaRemuxer.BuildStreamMap(Mp4Streams), Edits((0, "fre")));

            args.Should().Contain(" -c copy ");
            args.Should().NotContain("-c:v ");
            args.Should().NotContain("-c:a ");
            args.Should().NotContain("libx");
        }

        [Test]
        public void should_map_all_with_ignore_unknown_when_the_layout_is_unknown()
        {
            var args = FfmpegMatroskaRemuxer.BuildArguments(Source, Target, FfmpegMatroskaRemuxer.BuildStreamMap(null), Edits((0, "fre")));

            args.Should().Contain(" -map 0 -ignore_unknown -c copy -metadata:s:a:0 language=fre ");
        }

        [Test]
        public void should_escape_quotes_in_paths()
        {
            var args = FfmpegMatroskaRemuxer.BuildArguments("/m/a \"b\".mp4", "/m/a \"b\".krzw-remux.tmp.mkv", FfmpegMatroskaRemuxer.BuildStreamMap(null), Edits((0, "fre")));

            args.Should().Contain("-i \"/m/a \\\"b\\\".mp4\"");
            args.Should().EndWith("\"/m/a \\\"b\\\".krzw-remux.tmp.mkv\"");
        }

        // ----- process -----

        [Test]
        public void should_run_ffmpeg_through_the_process_provider_and_return_the_map()
        {
            GivenProcess("true");

            var map = Subject.Remux(Source, Target, Mp4Streams, Edits((1, "fre")), TimeSpan.FromSeconds(10));

            _binary.Should().Be("ffmpeg");
            _args.Should().Be(FfmpegMatroskaRemuxer.BuildArguments(Source, Target, FfmpegMatroskaRemuxer.BuildStreamMap(Mp4Streams), Edits((1, "fre"))));
            map.Kept.Should().Equal(0, 1, 2);
        }

        [Test]
        public void should_prefer_bundled_binary()
        {
            var bundled = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(bundled)).Returns(true);

            Subject.ResolveBinary().Should().Be(bundled);
        }

        [Test]
        public void should_throw_with_the_stderr_tail_on_failure()
        {
            GivenProcess("sh", "-c \"exit 1\"", "Guessed Channel Layout for Input Stream #0.1 : stereo", "[matroska @ 0x1] Subtitle codec 94213 is not supported.", "Could not write header for output file #0 (incorrect codec parameters ?): Invalid argument");

            var ex = Assert.Throws<AudioTrackRetagException>(() => Subject.Remux(Source, Target, Mp4Streams, Edits((1, "fre")), TimeSpan.FromSeconds(10)));

            ex.Message.Should().Contain("exited with code 1");
            ex.Message.Should().Contain("Subtitle codec 94213 is not supported");
            ex.Message.Should().Contain("Could not write header");
        }

        [Test]
        public void should_keep_only_the_last_lines_of_stderr()
        {
            var lines = new List<string>();

            for (var i = 0; i < 50; i++)
            {
                lines.Add($"line {i}");
            }

            GivenProcess("sh", "-c \"exit 1\"", lines.ToArray());

            var ex = Assert.Throws<AudioTrackRetagException>(() => Subject.Remux(Source, Target, Mp4Streams, Edits((1, "fre")), TimeSpan.FromSeconds(10)));

            ex.Message.Should().NotContain("line 0 ");
            ex.Message.Should().NotContain("line 41 ");
            ex.Message.Should().Contain("line 42");
            ex.Message.Should().Contain("line 49");
        }

        [Test]
        public void should_kill_and_throw_on_timeout()
        {
            GivenProcess("sleep", "30");

            Assert.Throws<AudioTrackRetagException>(() => Subject.Remux(Source, Target, Mp4Streams, Edits((1, "fre")), TimeSpan.FromSeconds(1)));

            Mocker.GetMock<IProcessProvider>().Verify(p => p.Kill(It.IsAny<int>()), Times.Once());
        }
    }
}
