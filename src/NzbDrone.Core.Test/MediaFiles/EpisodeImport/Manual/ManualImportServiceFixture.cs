using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Manual
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private Series _series;
        private EpisodeFile _episodeFile;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Id = 1)
                                     .With(s => s.Path = @"C:\Test\TV\Series".AsOsAgnostic())
                                     .Build();

            _episodeFile = Builder<EpisodeFile>.CreateNew()
                                               .With(f => f.Id = 1)
                                               .With(f => f.RelativePath = @"Season 01\episode.mkv")
                                               .With(f => f.Size = 123456789)
                                               .Build();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetSeries(_series.Id))
                  .Returns(_series);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeason(_series.Id, 1))
                  .Returns(new List<EpisodeFile> { _episodeFile });

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(_series.Id))
                  .Returns(new List<Episode>());

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormatForScoring(It.IsAny<EpisodeFile>(), It.IsAny<Series>()))
                  .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_not_throw_when_existing_file_is_missing_from_disk()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(false);

            List<ManualImportItem> items = null;

            Assert.DoesNotThrow(() => items = Subject.GetMediaFiles(_series.Id, 1));

            items.Should().HaveCount(1);
            items.First().Size.Should().Be(_episodeFile.Size);

            // The missing file must never be stat'd for size (that is what throws FileNotFoundException).
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileSize(It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_use_on_disk_size_when_existing_file_is_present()
        {
            var expectedPath = Path.Combine(_series.Path, _episodeFile.RelativePath);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFileSize(expectedPath))
                  .Returns(999);

            var items = Subject.GetMediaFiles(_series.Id, 1);

            items.Should().HaveCount(1);
            items.First().Size.Should().Be(999);
        }

        // krzw(symlink-import-guard): the final Manual Import step builds approved decisions without running the
        // specs. Wire the real import funnel in and make sure a symlinked source still never becomes an episode file.
        private ManualImportCommand GivenManualImportOfSymlink(string downloadId = null)
        {
            var path = @"C:\seed\Series.S01E01.1080p-GROUP\Series.S01E01.mkv".AsOsAgnostic();
            var episode = Builder<Episode>.CreateNew().With(e => e.Id = 7).With(e => e.SeriesId = _series.Id).Build();

            _series.QualityProfile = null;

            Mocker.SetConstant<IImportApprovedEpisodes>(Mocker.Resolve<ImportApprovedEpisodes>());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodes(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Episode> { episode });

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalEpisode>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalEpisode, DownloadClientItem>((l, d) => l);

            Mocker.GetMock<ISymlinkImportGuard>()
                  .Setup(s => s.Check(It.Is<LocalEpisode>(l => l.Path == path)))
                  .Returns(new SymlinkImportCheck
                  {
                      Target = @"C:\Test\TV\Series\Season 01\Series - S01E02.mkv".AsOsAgnostic(),
                      Reason = ImportRejectionReason.SourceIsSymlinkToLibraryFile,
                      Message = "Source is a symbolic link → target, which is already the library file of Series S01E02"
                  });

            return new ManualImportCommand
            {
                ImportMode = ImportMode.Auto,
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile
                    {
                        Path = path,
                        SeriesId = _series.Id,
                        EpisodeIds = new List<int> { episode.Id },
                        Quality = new QualityModel(Quality.WEBDL1080p),
                        Languages = new List<Language> { Language.English },
                        DownloadId = downloadId
                    }
                }
            };
        }

        [Test]
        public void should_not_import_a_symlinked_source_from_an_untracked_manual_import()
        {
            // The receiver is evaluated before the argument: wire the real funnel in before Subject is built
            var command = GivenManualImportOfSymlink();

            Subject.Execute(command);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<EpisodeFile>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_import_a_symlinked_source_from_a_queue_manual_import()
        {
            var downloadItem = Builder<DownloadClientItem>.CreateNew()
                                                           .With(d => d.DownloadId = "abc123")
                                                           .With(d => d.OutputPath = new OsPath(@"C:\seed\Series.S01E01.1080p-GROUP".AsOsAgnostic()))
                                                           .Build();

            var trackedDownload = Builder<TrackedDownload>.CreateNew()
                                                         .With(t => t.State = TrackedDownloadState.ImportBlocked)
                                                         .With(t => t.DownloadItem = downloadItem)
                                                         .With(t => t.ImportItem = downloadItem)
                                                         .With(t => t.RemoteEpisode = new RemoteEpisode { Episodes = new List<Episode> { new Episode { Id = 7 } } })
                                                         .Build();

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.Find("abc123"))
                  .Returns(trackedDownload);

            var command = GivenManualImportOfSymlink("abc123");

            Subject.Execute(command);

            Mocker.GetMock<IUpgradeMediaFiles>().Verify(v => v.UpgradeEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Add(It.IsAny<EpisodeFile>()), Times.Never());
            trackedDownload.State.Should().NotBe(TrackedDownloadState.Imported);

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
