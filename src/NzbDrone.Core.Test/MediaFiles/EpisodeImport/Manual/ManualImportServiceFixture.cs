using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport.Manual;
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
                  .Setup(s => s.ParseCustomFormat(It.IsAny<EpisodeFile>(), It.IsAny<Series>()))
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
    }
}
