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
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.AudioTags
{
    [TestFixture]
    public class MkvPropEditTrackTaggerFixture : CoreTest<MkvPropEditTrackTagger>
    {
        private const string Path = "/tv/Series/Season 01/Series - S01E01.mkv";
        private string _binary;
        private string _args;
        private Process _spawned;

        [SetUp]
        public void Setup()
        {
            _binary = null;
            _args = null;
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

        private void GivenProcess(string command, string arguments = null)
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) =>
                  {
                      _binary = path;
                      _args = args;
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

        [Test]
        public void should_build_one_edit_per_track_with_one_based_audio_numbers()
        {
            var args = MkvPropEditTrackTagger.BuildArguments(Path, Edits((0, "fre"), (2, "ger")));

            args.Should().Be("\"/tv/Series/Season 01/Series - S01E01.mkv\" --edit track:a1 --set language=fre --edit track:a3 --set language=ger");
        }

        [Test]
        public void should_order_edits_by_stream_index()
        {
            var args = MkvPropEditTrackTagger.BuildArguments(Path, Edits((3, "fre"), (1, "ger")));

            args.Should().Be("\"/tv/Series/Season 01/Series - S01E01.mkv\" --edit track:a2 --set language=ger --edit track:a4 --set language=fre");
        }

        [Test]
        public void should_escape_quotes_in_path()
        {
            MkvPropEditTrackTagger.BuildArguments("/movies/A \"quoted\" name.mkv", Edits((0, "fre")))
                                  .Should().StartWith("\"/movies/A \\\"quoted\\\" name.mkv\" --edit");
        }

        [Test]
        public void should_never_contain_shell_metacharacters_beyond_the_argument_list()
        {
            var args = MkvPropEditTrackTagger.BuildArguments(Path, Edits((0, "fre")));

            args.Should().NotContain("&&").And.NotContain("|").And.NotContain(";");
        }

        [Test]
        public void should_prefer_bundled_binary()
        {
            var bundled = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mkvpropedit");
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(bundled)).Returns(true);

            Subject.ResolveBinary().Should().Be(bundled);
        }

        [Test]
        public void should_fall_back_to_path()
        {
            Subject.ResolveBinary().Should().Be("mkvpropedit");
        }

        [Test]
        public void should_run_mkvpropedit_through_the_process_provider()
        {
            GivenProcess("true");

            Subject.SetLanguages(Path, Edits((0, "fre")), TimeSpan.FromSeconds(10));

            _binary.Should().Be("mkvpropedit");
            _args.Should().Be(MkvPropEditTrackTagger.BuildArguments(Path, Edits((0, "fre"))));
        }

        [Test]
        public void should_do_nothing_without_edits()
        {
            GivenProcess("true");

            Subject.SetLanguages(Path, new List<AudioTrackRetagTrack>(), TimeSpan.FromSeconds(10));

            Mocker.GetMock<IProcessProvider>().Verify(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()), Times.Never());
        }

        [Test]
        public void should_throw_on_error_exit_code()
        {
            GivenProcess("sh", "-c \"exit 2\"");

            Assert.Throws<AudioTrackRetagException>(() => Subject.SetLanguages(Path, Edits((0, "fre")), TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void should_tolerate_warning_exit_code()
        {
            GivenProcess("sh", "-c \"exit 1\"");

            Subject.SetLanguages(Path, Edits((0, "fre")), TimeSpan.FromSeconds(10));

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_kill_and_throw_on_timeout()
        {
            GivenProcess("sleep", "30");

            Assert.Throws<AudioTrackRetagException>(() => Subject.SetLanguages(Path, Edits((0, "fre")), TimeSpan.FromSeconds(1)));

            Mocker.GetMock<IProcessProvider>().Verify(p => p.Kill(It.IsAny<int>()), Times.Once());
        }
    }
}
