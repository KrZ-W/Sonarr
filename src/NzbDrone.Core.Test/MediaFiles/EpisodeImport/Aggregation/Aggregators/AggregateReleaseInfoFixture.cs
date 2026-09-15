using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    [TestFixture]
    public class AggregateReleaseInfoFixture : CoreTest<AggregateReleaseInfo>
    {
        private LocalEpisode _localEpisode;
        private DownloadClientItem _downloadClientItem;

        [SetUp]
        public void Setup()
        {
            _localEpisode = new LocalEpisode();
            _downloadClientItem = new DownloadClientItem { DownloadId = "DOWNLOAD" };
        }

        private void GivenGrabbedHistory(string sourceTitle)
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("DOWNLOAD"))
                  .Returns(new List<EpisodeHistory>
                  {
                      new EpisodeHistory
                      {
                          EventType = EpisodeHistoryEventType.Grabbed,
                          Date = DateTime.UtcNow,
                          DownloadId = "DOWNLOAD",
                          SourceTitle = sourceTitle,
                          Data = new Dictionary<string, string>()
                      }
                  });
        }

        // krzw(grabbed-release-title)
        [Test]
        public void should_set_the_grabbed_release_title_from_the_grab_history()
        {
            GivenGrabbedHistory("Series.Title.S01E01.MULTi.1080p.WEB.H264-GROUP");

            Subject.Aggregate(_localEpisode, _downloadClientItem)
                   .GrabbedReleaseTitle.Should().Be("Series.Title.S01E01.MULTi.1080p.WEB.H264-GROUP");
        }

        [Test]
        public void should_sanitize_a_tracker_description_blob()
        {
            GivenGrabbedHistory("Series.Title.S01E01-GROUP\r\n\t\r\n\tTaille: 4 GB Seeders: 27");

            Subject.Aggregate(_localEpisode, _downloadClientItem)
                   .GrabbedReleaseTitle.Should().Be("Series.Title.S01E01-GROUP");
        }

        [Test]
        public void should_not_set_the_grabbed_release_title_without_a_download_client_item()
        {
            Subject.Aggregate(_localEpisode, null).GrabbedReleaseTitle.Should().BeNull();
        }

        [Test]
        public void should_not_set_the_grabbed_release_title_when_there_is_no_grab_history()
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<EpisodeHistory>());

            Subject.Aggregate(_localEpisode, _downloadClientItem).GrabbedReleaseTitle.Should().BeNull();
        }
    }
}
