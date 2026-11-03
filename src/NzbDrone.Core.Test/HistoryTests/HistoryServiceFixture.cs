using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.HistoryTests
{
    public class HistoryServiceFixture : CoreTest<HistoryService>
    {
        private QualityProfile _profile;
        private QualityProfile _profileCustom;

        [SetUp]
        public void Setup()
        {
            _profile = new QualityProfile
                {
                    Cutoff = Quality.WEBDL720p.Id,
                    Items = QualityFixture.GetDefaultQualities(),
                };

            _profileCustom = new QualityProfile
                {
                    Cutoff = Quality.WEBDL720p.Id,
                    Items = QualityFixture.GetDefaultQualities(Quality.DVD),
                };
        }

        [Test]
        public void should_use_file_name_for_source_title_if_scene_name_is_null()
        {
            var series = Builder<Series>.CreateNew().Build();
            var episodes = Builder<Episode>.CreateListOfSize(1).Build().ToList();
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.SceneName = null)
                                                  .Build();

            var localEpisode = new LocalEpisode
                               {
                                   Series = series,
                                   Episodes = episodes,
                                   Path = @"C:\Test\Unsorted\Series.s01e01.mkv"
                               };

            var downloadClientItem = new DownloadClientItem
                                     {
                                         DownloadClientInfo = new DownloadClientItemClientInfo
                                         {
                                             Protocol = DownloadProtocol.Usenet,
                                             Id = 1,
                                             Name = "sab"
                                         },
                                         DownloadId = "abcd"
                                     };

            Subject.Handle(new EpisodeImportedEvent(localEpisode, episodeFile, new List<DeletedEpisodeFile>(), true, downloadClientItem));

            Mocker.GetMock<IHistoryRepository>()
                .Verify(v => v.Insert(It.Is<EpisodeHistory>(h => h.SourceTitle == Path.GetFileNameWithoutExtension(localEpisode.Path))));
        }

        // krzw(atomic-upgrade) F11a: one episodeFileDeleted/Upgrade row per episode of the replaced file
        [Test]
        public void should_record_an_upgrade_delete_row_for_each_episode_of_the_old_file()
        {
            var episodes = Builder<Episode>.CreateListOfSize(2).Build().ToList();
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.Path = @"C:\Test\TV\Series\old.mkv")
                                                  .With(f => f.Episodes = episodes)
                                                  .Build();

            Subject.Handle(new EpisodeFileDeletedEvent(episodeFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IHistoryRepository>()
                  .Verify(v => v.Insert(It.Is<EpisodeHistory>(h => h.EventType == EpisodeHistoryEventType.EpisodeFileDeleted && h.Data["Reason"] == "Upgrade")), Times.Exactly(2));
        }

        [Test]
        public void should_record_nothing_when_the_deleted_file_has_no_episodes_attached()
        {
            // The pre-fix symptom: the lazy load resolved after the episodes were re-pointed
            var episodeFile = Builder<EpisodeFile>.CreateNew()
                                                  .With(f => f.Episodes = new List<Episode>())
                                                  .Build();

            Subject.Handle(new EpisodeFileDeletedEvent(episodeFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IHistoryRepository>().Verify(v => v.Insert(It.IsAny<EpisodeHistory>()), Times.Never());
        }
    }
}
