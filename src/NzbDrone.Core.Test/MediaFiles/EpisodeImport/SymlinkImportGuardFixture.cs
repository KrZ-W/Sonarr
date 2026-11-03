using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport
{
    // krzw(symlink-import-guard)
    [TestFixture]
    public class SymlinkImportGuardFixture : CoreTest<SymlinkImportGuard>
    {
        private Series _series;
        private Series _otherSeries;
        private Episode _episode;
        private EpisodeFile _currentFile;
        private EpisodeFile _otherFile;
        private LocalEpisode _localEpisode;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Id = 5)
                                     .With(s => s.Title = "Knight Rider")
                                     .With(s => s.Path = @"C:\Test\TV\Knight Rider (1982)".AsOsAgnostic())
                                     .Build();

            _otherSeries = Builder<Series>.CreateNew()
                                          .With(s => s.Id = 6)
                                          .With(s => s.Title = "Other Series")
                                          .With(s => s.Path = @"C:\Test\TV\Other Series".AsOsAgnostic())
                                          .Build();

            _currentFile = Builder<EpisodeFile>.CreateNew()
                                               .With(f => f.Id = 10)
                                               .With(f => f.SeriesId = _series.Id)
                                               .With(f => f.Path = null)
                                               .With(f => f.RelativePath = @"Season 1\Knight Rider - S01E11 - The Final Verdict.mkv".AsOsAgnostic())
                                               .Build();

            _otherFile = Builder<EpisodeFile>.CreateNew()
                                             .With(f => f.Id = 11)
                                             .With(f => f.SeriesId = _series.Id)
                                             .With(f => f.Path = null)
                                             .With(f => f.RelativePath = @"Season 1\Knight Rider - S01E12 - A Plush Ride.mkv".AsOsAgnostic())
                                             .Build();

            _episode = Builder<Episode>.CreateNew()
                                       .With(e => e.Id = 100)
                                       .With(e => e.SeasonNumber = 1)
                                       .With(e => e.EpisodeNumber = 11)
                                       .With(e => e.EpisodeFileId = _currentFile.Id)
                                       .Build();

            _localEpisode = new LocalEpisode
            {
                Path = @"C:\seed\Knight.Rider.S01E11.MULTi.1080p.WEBrip-TyHD\Knight.Rider.S01E11.mkv".AsOsAgnostic(),
                Series = _series,
                Episodes = new List<Episode> { _episode }
            };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.RejectSymlinkImportSources)
                  .Returns(true);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFiles(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<EpisodeFile> { _currentFile });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesWithRelativePath(It.IsAny<int>(), It.IsAny<string>()))
                  .Returns(new List<EpisodeFile>());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesWithRelativePath(_series.Id, _otherFile.RelativePath))
                  .Returns(new List<EpisodeFile> { _otherFile });

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeriesPaths())
                  .Returns(new Dictionary<int, string> { { _series.Id, _series.Path }, { _otherSeries.Id, _otherSeries.Path } });

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(_series.Id))
                  .Returns(_series);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesByFileId(_currentFile.Id))
                  .Returns(new List<Episode> { _episode });

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesByFileId(_otherFile.Id))
                  .Returns(new List<Episode> { new Episode { SeasonNumber = 1, EpisodeNumber = 12 } });
        }

        private void GivenLinkTo(string target)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetSymbolicLinkTarget(_localEpisode.Path))
                  .Returns(target);
        }

        private string LibraryPath(EpisodeFile file)
        {
            return Path.Combine(_series.Path, file.RelativePath);
        }

        [Test]
        public void should_return_null_for_regular_file()
        {
            Subject.Check(_localEpisode).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_setting_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RejectSymlinkImportSources).Returns(false);
            GivenLinkTo(LibraryPath(_currentFile));

            Subject.Check(_localEpisode).Should().BeNull();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetSymbolicLinkTarget(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_null_for_a_candidate_without_path()
        {
            Subject.Check(new LocalEpisode()).Should().BeNull();
            Subject.Check(null).Should().BeNull();
        }

        [Test]
        public void should_report_plain_symlink_when_target_is_not_in_the_library()
        {
            var target = @"C:\somewhere\else.mkv".AsOsAgnostic();
            GivenLinkTo(target);

            var check = Subject.Check(_localEpisode);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
            check.Target.Should().Be(target);
            check.LibraryFile.Should().BeNull();
            check.IsCurrentFile.Should().BeFalse();
            check.Message.Should().Be($"Source is a symbolic link → {target}");
        }

        [Test]
        public void should_report_current_file_when_target_is_the_episodes_own_file()
        {
            GivenLinkTo(LibraryPath(_currentFile));

            var check = Subject.Check(_localEpisode);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToCurrentFile);
            check.IsCurrentFile.Should().BeTrue();
            check.LibraryFile.Id.Should().Be(_currentFile.Id);
            check.Message.Should().Be($"Source is a symbolic link → {LibraryPath(_currentFile)}, which is already the current file of this episode");
        }

        [Test]
        public void should_report_current_file_for_a_multi_episode_file_shared_by_all_mapped_episodes()
        {
            var second = Builder<Episode>.CreateNew().With(e => e.Id = 101).With(e => e.EpisodeFileId = _currentFile.Id).Build();
            _localEpisode.Episodes.Add(second);
            GivenLinkTo(LibraryPath(_currentFile));

            var check = Subject.Check(_localEpisode);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToCurrentFile);
            check.Message.Should().EndWith("the current file of these episodes");
        }

        [Test]
        public void should_report_library_file_when_only_some_mapped_episodes_own_the_target()
        {
            var second = Builder<Episode>.CreateNew().With(e => e.Id = 101).With(e => e.EpisodeFileId = 0).Build();
            _localEpisode.Episodes.Add(second);
            GivenLinkTo(LibraryPath(_currentFile));

            Subject.Check(_localEpisode).Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);
        }

        [Test]
        public void should_report_library_file_of_another_episode_with_a_readable_description()
        {
            // The incident: a single-episode release numbered one off, whose seed payload was deduplicated
            // against the library copy of the NEXT episode.
            GivenLinkTo(LibraryPath(_otherFile));

            var check = Subject.Check(_localEpisode);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);
            check.IsCurrentFile.Should().BeFalse();
            check.LibraryFile.Id.Should().Be(_otherFile.Id);
            check.Message.Should().Be($"Source is a symbolic link → {LibraryPath(_otherFile)}, which is already the library file of Knight Rider S01E12");
        }

        [Test]
        public void should_find_a_library_file_of_another_series()
        {
            var foreignFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.Id = 12)
                                                  .With(f => f.SeriesId = _otherSeries.Id)
                                                  .With(f => f.Path = null)
                                                  .With(f => f.RelativePath = @"Season 2\Other Series - S02E03.mkv".AsOsAgnostic())
                                                  .Build();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesWithRelativePath(_otherSeries.Id, foreignFile.RelativePath))
                  .Returns(new List<EpisodeFile> { foreignFile });

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(_otherSeries.Id)).Returns(_otherSeries);
            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesByFileId(foreignFile.Id))
                  .Returns(new List<Episode> { new Episode { SeasonNumber = 2, EpisodeNumber = 3 } });

            GivenLinkTo(Path.Combine(_otherSeries.Path, foreignFile.RelativePath));

            var check = Subject.Check(_localEpisode);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);
            check.Message.Should().EndWith("which is already the library file of Other Series S02E03");
        }

        [Test]
        public void should_not_treat_an_untracked_file_in_a_series_folder_as_a_library_file()
        {
            GivenLinkTo(Path.Combine(_series.Path, "Season 1", "untracked.mkv"));

            Subject.Check(_localEpisode).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
        }

        [Test]
        public void should_report_plain_symlink_when_candidate_has_no_episodes_and_target_is_unknown()
        {
            _localEpisode.Episodes = null;
            GivenLinkTo(@"C:\somewhere\else.mkv".AsOsAgnostic());

            Subject.Check(_localEpisode).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
        }
    }
}
