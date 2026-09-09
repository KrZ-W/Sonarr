using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class FfmpegAudioClipExtractorFixture : CoreTest<FfmpegAudioClipExtractor>
    {
        private const string Source = "/downloads/movie.mkv";
        private string _clipPath;
        private Process _spawned;

        [SetUp]
        public void Setup()
        {
            _clipPath = null;
            _spawned = null;

            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.TempFolder).Returns("/tmp");

            // remember the temp path ffmpeg was asked to write
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _clipPath = args.Substring(args.LastIndexOf('"', args.Length - 2) + 1).TrimEnd('"'))
                  .Returns<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _spawned = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false }));

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.ReadAllBytes(It.IsAny<string>())).Returns(new byte[] { 1, 2, 3 });
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

        private void VerifyTempDeleted()
        {
            _clipPath.Should().NotBeNullOrEmpty();
            _clipPath.Should().StartWith("/tmp/sonarr-audio-language-").And.EndWith(".wav");
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(_clipPath), Times.Once());
        }

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
        public void should_prefer_the_bundled_ffmpeg_when_it_exists()
        {
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OsInfo.IsWindows ? "ffmpeg.exe" : "ffmpeg");

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(bundled)).Returns(true);
            Subject.ResolveBinary().Should().Be(bundled);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(bundled)).Returns(false);
            Subject.ResolveBinary().Should().Be(OsInfo.IsWindows ? "ffmpeg.exe" : "ffmpeg");
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_return_the_clip_bytes_and_delete_the_temp_file_on_success()
        {
            var bytes = Subject.Extract(Source, 0, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

            bytes.Should().Equal(new byte[] { 1, 2, 3 });
            Mocker.GetMock<IDiskProvider>().Verify(d => d.ReadAllBytes(_clipPath), Times.Once());
            VerifyTempDeleted();
        }

        [Test]
        public void should_return_null_warn_once_and_delete_the_temp_file_when_ffmpeg_cannot_start()
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _clipPath = args.Substring(args.LastIndexOf('"', args.Length - 2) + 1).TrimEnd('"'))
                  .Throws(new InvalidOperationException("no ffmpeg"));

            Subject.Extract(Source, 0, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)).Should().BeNull();

            VerifyTempDeleted();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_kill_ffmpeg_warn_once_and_delete_the_temp_file_on_timeout()
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _clipPath = args.Substring(args.LastIndexOf('"', args.Length - 2) + 1).TrimEnd('"'))
                  .Returns<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _spawned = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false }));

            Subject.Extract(Source, 0, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)).Should().BeNull();

            Mocker.GetMock<IProcessProvider>().Verify(p => p.Kill(_spawned.Id), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.ReadAllBytes(It.IsAny<string>()), Times.Never());
            VerifyTempDeleted();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_return_null_warn_once_and_delete_the_temp_file_on_non_zero_exit()
        {
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Callback<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _clipPath = args.Substring(args.LastIndexOf('"', args.Length - 2) + 1).TrimEnd('"'))
                  .Returns<string, string, StringDictionary, Action<string>, Action<string>>((path, args, env, o, e) => _spawned = Process.Start(new ProcessStartInfo("false") { UseShellExecute = false }));

            Subject.Extract(Source, 0, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)).Should().BeNull();

            VerifyTempDeleted();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_try_to_delete_a_temp_file_that_was_never_written()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(It.IsAny<string>())).Returns(false);
            Mocker.GetMock<IProcessProvider>()
                  .Setup(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()))
                  .Throws(new InvalidOperationException("no ffmpeg"));

            Subject.Extract(Source, 0, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)).Should().BeNull();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
