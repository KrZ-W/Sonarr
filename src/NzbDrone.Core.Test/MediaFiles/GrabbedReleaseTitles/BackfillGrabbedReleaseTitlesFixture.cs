using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class BackfillGrabbedReleaseTitlesFixture : CoreTest<BackfillGrabbedReleaseTitlesService>
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        private Series _series;
        private List<EpisodeFile> _files;
        private List<Episode> _episodes;
        private List<EpisodeHistory> _imports;
        private List<EpisodeHistory> _grabs;
        private List<EpisodeFile> _updated;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 7, Title = "Series Title" };
            _files = new List<EpisodeFile>();
            _episodes = new List<Episode>();
            _imports = new List<EpisodeHistory>();
            _grabs = new List<EpisodeHistory>();
            _updated = new List<EpisodeFile>();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(() => new List<Series> { _series });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesBySeries(_series.Id))
                  .Returns(() => _files);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(_series.Id))
                  .Returns(() => _episodes);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByEventType(EpisodeHistoryEventType.DownloadFolderImported))
                  .Returns(() => _imports);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.GetByEventType(EpisodeHistoryEventType.Grabbed))
                  .Returns(() => _grabs);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Update(It.IsAny<List<EpisodeFile>>()))
                  .Callback<List<EpisodeFile>>(files => _updated.AddRange(files));
        }

        private EpisodeFile GivenFile(int id, DateTime dateAdded, string grabbedReleaseTitle = null)
        {
            var file = new EpisodeFile
            {
                Id = id,
                SeriesId = _series.Id,
                DateAdded = dateAdded,
                RelativePath = $"Season 01/episode{id}.mkv",
                GrabbedReleaseTitle = grabbedReleaseTitle
            };

            _files.Add(file);
            _episodes.Add(new Episode { Id = 100 + id, SeriesId = _series.Id, EpisodeFileId = id });

            return file;
        }

        private void GivenImport(DateTime date, string downloadId, int? fileId = null, int episodeId = 0)
        {
            var data = new Dictionary<string, string>();

            if (fileId.HasValue)
            {
                data["fileId"] = fileId.Value.ToString();
            }

            _imports.Add(new EpisodeHistory
            {
                Id = _imports.Count + 1,
                SeriesId = _series.Id,
                EpisodeId = episodeId,
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                Date = date,
                DownloadId = downloadId,
                Data = data
            });
        }

        private void GivenGrab(DateTime date, string downloadId, string sourceTitle)
        {
            _grabs.Add(new EpisodeHistory
            {
                Id = 1000 + _grabs.Count,
                SeriesId = _series.Id,
                EventType = EpisodeHistoryEventType.Grabbed,
                Date = date,
                DownloadId = downloadId,
                SourceTitle = sourceTitle,
                Data = new Dictionary<string, string>()
            });
        }

        [Test]
        public void should_prefer_the_fileid_oracle_over_a_closer_import_in_time()
        {
            var file = GivenFile(1, Now);

            // The neighbouring torrent imported one minute away, the real import an hour away.
            GivenImport(Now.AddMinutes(-1), "OTHERDOWNLOAD", episodeId: 101);
            GivenImport(Now.AddHours(-1), "REALDOWNLOAD", fileId: 1, episodeId: 101);

            GivenGrab(Now.AddHours(-2), "OTHERDOWNLOAD", "Wrong.Series.S01E01-WRONG");
            GivenGrab(Now.AddHours(-3), "REALDOWNLOAD", "Series.Title.S01E01.MULTi.1080p-RIGHT");

            var result = Subject.Backfill();

            file.GrabbedReleaseTitle.Should().Be("Series.Title.S01E01.MULTi.1080p-RIGHT");
            result.Set.Should().Be(1);
            result.Scanned.Should().Be(1);
        }

        [Test]
        public void should_skip_a_file_whose_own_import_has_no_download_id()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now, null, fileId: 1, episodeId: 101);
            GivenImport(Now.AddMinutes(-1), "OTHERDOWNLOAD", episodeId: 101);
            GivenGrab(Now.AddHours(-1), "OTHERDOWNLOAD", "Wrong.Series.S01E01-WRONG");

            var result = Subject.Backfill();

            file.GrabbedReleaseTitle.Should().BeNull();
            result.NoDownloadId.Should().Be(1);
            result.Set.Should().Be(0);
            _updated.Should().BeEmpty();
        }

        [Test]
        public void should_fall_back_to_the_closest_import_within_six_hours()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now.AddHours(-5), "CLOSEENOUGH", episodeId: 101);
            GivenImport(Now.AddHours(-9), "TOOFARAWAY", episodeId: 101);

            GivenGrab(Now.AddHours(-6), "CLOSEENOUGH", "Series.Title.S01E01-CLOSE");
            GivenGrab(Now.AddHours(-10), "TOOFARAWAY", "Series.Title.S01E01-FAR");

            Subject.Backfill();

            file.GrabbedReleaseTitle.Should().Be("Series.Title.S01E01-CLOSE");
        }

        [Test]
        public void should_not_match_an_import_outside_the_six_hour_window()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now.AddHours(-9), "TOOFARAWAY", episodeId: 101);
            GivenGrab(Now.AddHours(-10), "TOOFARAWAY", "Series.Title.S01E01-FAR");

            var result = Subject.Backfill();

            file.GrabbedReleaseTitle.Should().BeNull();
            result.NoImportEvent.Should().Be(1);
        }

        [Test]
        public void should_match_the_download_id_case_insensitively()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now, "abcdef123456", fileId: 1, episodeId: 101);
            GivenGrab(Now.AddHours(-1), "ABCDEF123456", "Series.Title.S01E01-RIGHT");

            Subject.Backfill();

            file.GrabbedReleaseTitle.Should().Be("Series.Title.S01E01-RIGHT");
        }

        [Test]
        public void should_leave_the_file_untouched_when_there_is_no_grab()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now, "NOGRABFORTHIS", fileId: 1, episodeId: 101);

            var result = Subject.Backfill();

            file.GrabbedReleaseTitle.Should().BeNull();
            result.NoGrab.Should().Be(1);
            _updated.Should().BeEmpty();
        }

        [Test]
        public void should_sanitize_the_stored_title()
        {
            var file = GivenFile(1, Now);

            GivenImport(Now, "DOWNLOAD", fileId: 1, episodeId: 101);
            GivenGrab(Now.AddHours(-1), "DOWNLOAD", "Series.Title.S01E01-GROUP\r\n\t\r\n\tTaille: 4 GB Seeders: 27");

            Subject.Backfill();

            file.GrabbedReleaseTitle.Should().Be("Series.Title.S01E01-GROUP");
        }

        [Test]
        public void should_report_an_already_correct_file_as_unchanged_and_not_write_it()
        {
            GivenFile(1, Now, "Series.Title.S01E01-GROUP");

            GivenImport(Now, "DOWNLOAD", fileId: 1, episodeId: 101);
            GivenGrab(Now.AddHours(-1), "DOWNLOAD", "Series.Title.S01E01-GROUP");

            var result = Subject.Backfill();

            result.Unchanged.Should().Be(1);
            result.Set.Should().Be(0);
            _updated.Should().BeEmpty();
        }

        [Test]
        public void should_be_idempotent()
        {
            GivenFile(1, Now);

            GivenImport(Now, "DOWNLOAD", fileId: 1, episodeId: 101);
            GivenGrab(Now.AddHours(-1), "DOWNLOAD", "Series.Title.S01E01-GROUP");

            Subject.Backfill().Set.Should().Be(1);
            Subject.Backfill().Set.Should().Be(0);

            _updated.Select(f => f.Id).Should().BeEquivalentTo(new[] { 1 });
        }

        [Test]
        public void should_ignore_an_unparseable_file_id_and_fall_back_to_time_proximity()
        {
            var file = GivenFile(1, Now);

            _imports.Add(new EpisodeHistory
            {
                Id = 1,
                SeriesId = _series.Id,
                EpisodeId = 101,
                EventType = EpisodeHistoryEventType.DownloadFolderImported,
                Date = Now,
                DownloadId = "DOWNLOAD",
                Data = new Dictionary<string, string> { { "fileId", "not-a-number" } }
            });

            GivenGrab(Now.AddHours(-1), "DOWNLOAD", "Series.Title.S01E01-GROUP");

            Subject.Backfill();

            file.GrabbedReleaseTitle.Should().Be("Series.Title.S01E01-GROUP");
        }
    }
}
