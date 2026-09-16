using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeFileMovingServiceTests
{
    [TestFixture]
    public class MoveEpisodeFileFixture : CoreTest<EpisodeFileMovingService>
    {
        private Series _series;
        private EpisodeFile _episodeFile;
        private LocalEpisode _localEpisode;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Path = @"C:\Test\TV\Series".AsOsAgnostic())
                                     .Build();

            _episodeFile = Builder<EpisodeFile>.CreateNew()
                                               .With(f => f.Path = null)
                                               .With(f => f.RelativePath = @"Season 1\File.avi")
                                               .Build();

            _localEpisode = Builder<LocalEpisode>.CreateNew()
                                                 .With(l => l.Series = _series)
                                                 .With(l => l.Episodes = Builder<Episode>.CreateListOfSize(1).Build().ToList())
                                                 .Build();

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFilePath(It.IsAny<List<Episode>>(), It.IsAny<Series>(), It.IsAny<EpisodeFile>(), It.IsAny<string>(), It.IsAny<NamingConfig>(), It.IsAny<List<CustomFormat>>()))
                  .Returns(@"C:\Test\TV\Series\Season 01\File Name.avi".AsOsAgnostic());

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildSeasonPath(It.IsAny<Series>(), It.IsAny<int>()))
                  .Returns(@"C:\Test\TV\Series\Season 01".AsOsAgnostic());

            var rootFolder = @"C:\Test\TV\".AsOsAgnostic();

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>()))
                .Returns(rootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);
        }

        // krzw(grabbed-release-title): naming is the feature's single carve-out. The {Custom Formats} token must be
        // built from the LEGACY ladder (NamingCustomFormats), never from the scoring ladder that CustomFormats now
        // holds, so turning the setting on can never rename an existing library.
        [Test]
        public void should_build_the_file_path_from_the_naming_custom_formats()
        {
            // Distinct ids: CustomFormat compares by id, so two id-0 formats would match each other in Moq.
            var namingFormats = new List<CustomFormat> { new CustomFormat("Naming") { Id = 1 } };
            var scoringFormats = new List<CustomFormat> { new CustomFormat("Scoring") { Id = 2 } };

            _localEpisode.CustomFormats = scoringFormats;
            _localEpisode.NamingCustomFormats = namingFormats;

            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFilePath(It.IsAny<List<Episode>>(), It.IsAny<Series>(), It.IsAny<EpisodeFile>(), It.IsAny<string>(), It.IsAny<NamingConfig>(), namingFormats), Times.Once());

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFilePath(It.IsAny<List<Episode>>(), It.IsAny<Series>(), It.IsAny<EpisodeFile>(), It.IsAny<string>(), It.IsAny<NamingConfig>(), scoringFormats), Times.Never());
        }

        // krzw(grabbed-release-title)
        [Test]
        public void should_fall_back_to_custom_formats_for_naming_when_the_naming_formats_were_never_set()
        {
            var formats = new List<CustomFormat> { new CustomFormat("Only") { Id = 3 } };

            _localEpisode.CustomFormats = formats;

            Subject.CopyEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFilePath(It.IsAny<List<Episode>>(), It.IsAny<Series>(), It.IsAny<EpisodeFile>(), It.IsAny<string>(), It.IsAny<NamingConfig>(), formats), Times.Once());
        }

        [Test]
        public void should_catch_UnauthorizedAccessException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<UnauthorizedAccessException>();

            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);
        }

        [Test]
        public void should_catch_InvalidOperationException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<InvalidOperationException>();

            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);
        }

        [Test]
        public void should_notify_on_series_folder_creation()
        {
            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<EpisodeFolderCreatedEvent>(It.Is<EpisodeFolderCreatedEvent>(p =>
                      p.SeriesFolder.IsNotNullOrWhiteSpace())), Times.Once());
        }

        [Test]
        public void should_notify_on_season_folder_creation()
        {
            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<EpisodeFolderCreatedEvent>(It.Is<EpisodeFolderCreatedEvent>(p =>
                      p.SeasonFolder.IsNotNullOrWhiteSpace())), Times.Once());
        }

        [Test]
        public void should_not_notify_if_series_folder_already_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_series.Path))
                  .Returns(true);

            Subject.MoveEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<EpisodeFolderCreatedEvent>(It.Is<EpisodeFolderCreatedEvent>(p =>
                      p.SeriesFolder.IsNotNullOrWhiteSpace())), Times.Never());
        }
    }
}
