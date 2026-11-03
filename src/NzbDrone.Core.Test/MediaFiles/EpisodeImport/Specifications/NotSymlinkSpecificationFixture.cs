using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Specifications
{
    // krzw(symlink-import-guard)
    [TestFixture]
    public class NotSymlinkSpecificationFixture : CoreTest<NotSymlinkSpecification>
    {
        private LocalEpisode _localEpisode;
        private string _target;

        [SetUp]
        public void Setup()
        {
            _localEpisode = new LocalEpisode
            {
                Path = @"C:\Test\Downloads\Shared.Torrent\Series.Title.S01E01.1080p.BluRay.x264-GROUP.mkv".AsOsAgnostic(),
                Size = 100,
                Series = Builder<Series>.CreateNew().Build()
            };

            _target = @"C:\Test\TV\Other Series {tvdb-12345}\Season 1\Other.Series.S01E01.mkv".AsOsAgnostic();
        }

        private SymlinkImportCheck GivenSymlink(ImportRejectionReason reason = ImportRejectionReason.SourceIsSymlink)
        {
            var check = new SymlinkImportCheck
            {
                Target = _target,
                Reason = reason,
                Message = $"Source is a symbolic link → {_target}"
            };

            Mocker.GetMock<ISymlinkImportGuard>()
                  .Setup(s => s.Check(_localEpisode))
                  .Returns(check);

            return check;
        }

        [Test]
        public void should_accept_when_guard_finds_no_link()
        {
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();

            Mocker.GetMock<ISymlinkImportGuard>().Verify(s => s.Check(_localEpisode), Times.Once());
        }

        [Test]
        public void should_reject_symlink_with_the_guards_reason_and_message()
        {
            GivenSymlink();

            var result = Subject.IsSatisfiedBy(_localEpisode, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
            result.Message.Should().Be($"Source is a symbolic link → {_target}");

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_pass_through_the_library_file_reason()
        {
            GivenSymlink(ImportRejectionReason.SourceIsSymlinkToLibraryFile);

            Subject.IsSatisfiedBy(_localEpisode, null).Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_reject_link_to_own_current_file_without_warning()
        {
            // Completed download handling turns this rejection into "imported"; a Warn per pass would be noise.
            GivenSymlink(ImportRejectionReason.SourceIsSymlinkToCurrentFile);

            var result = Subject.IsSatisfiedBy(_localEpisode, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToCurrentFile);

            ExceptionVerification.ExpectedWarns(0);
        }

        [Test]
        public void should_log_warning_naming_link_and_target()
        {
            var memoryTarget = new MemoryTarget { Layout = "${level}|${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(memoryTarget);
            var logFactory = new LogFactory { Configuration = configuration };

            Mocker.SetConstant(logFactory.GetLogger(nameof(NotSymlinkSpecification)));

            GivenSymlink();

            Subject.IsSatisfiedBy(_localEpisode, null);

            memoryTarget.Logs.Should().ContainSingle()
                        .Which.Should().StartWith("Warn|")
                        .And.Contain(_localEpisode.Path)
                        .And.Contain(_target);
        }

        [Test]
        public void should_reject_symlink_in_series_folder_too()
        {
            _localEpisode.ExistingFile = true;
            GivenSymlink();

            Subject.IsSatisfiedBy(_localEpisode, null).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
