using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Core.Tv.ImdbTitles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests.ImdbTitleTests
{
    [TestFixture]
    public class ImdbTitleSyncServiceFixture : CoreTest<ImdbTitleSyncService>
    {
        private Series _series;
        private List<ImdbAkasRow> _rows;
        private List<SceneMapping> _mappings;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 1, TvdbId = 81189, ImdbId = "tt0903747", Title = "Breaking Bad", Year = 2008 };
            _rows = new List<ImdbAkasRow>();
            _mappings = new List<SceneMapping>();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderRegions).Returns("CA,FR");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderLanguages).Returns("fr");

            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetTitles("tt0903747")).Returns(() => _rows);

            Mocker.GetMock<ISceneMappingService>().Setup(s => s.FindByTvdbId(81189)).Returns(() => _mappings);
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(() => new List<Series> { _series });

            Mocker.GetMock<IUserSceneMappingImportService>()
                  .Setup(s => s.Import(It.IsAny<List<UserSceneMappingImportRequest>>()))
                  .Returns((List<UserSceneMappingImportRequest> r) => new UserSceneMappingImportResult { SeriesProcessed = r.Count, TitlesAdded = r.Sum(x => x.Titles.Count) });
        }

        private static ImdbAkasRow Row(string title, string region = null, string language = null)
        {
            return new ImdbAkasRow { Tconst = "tt0903747", Title = title, Region = region, Language = language };
        }

        private List<UserSceneMappingImportRequest> Requests()
        {
            var captured = new List<UserSceneMappingImportRequest>();
            Mocker.GetMock<IUserSceneMappingImportService>().Invocations
                  .Where(i => i.Method.Name == nameof(IUserSceneMappingImportService.Import))
                  .ToList()
                  .ForEach(i => captured.AddRange((List<UserSceneMappingImportRequest>)i.Arguments[0]));
            return captured;
        }

        [Test]
        public void should_build_request_with_series_identity_and_region_as_is()
        {
            _rows.Add(Row("Le Chimiste d'Albuquerque", "CA"));
            _rows.Add(Row("Breaking Bad : Le chimiste", "FR"));
            _rows.Add(Row("Chimie mortelle", "BE", "fr"));

            var request = Subject.BuildRequest(_series);

            request.TvdbId.Should().Be(81189);
            request.ImdbId.Should().Be("tt0903747");
            request.SeriesTitle.Should().Be("Breaking Bad");
            request.Year.Should().Be(2008);
            request.Titles.Select(t => t.Title).Should().Equal("Le Chimiste d'Albuquerque", "Breaking Bad : Le chimiste", "Chimie mortelle");
            request.Titles.Select(t => t.Region).Should().Equal("CA", "FR", "BE");
        }

        [Test]
        public void should_skip_titles_the_series_already_has_using_loose_normalisation()
        {
            _mappings.Add(new SceneMapping { Title = "Le chimiste d Albuquerque", TvdbId = 81189 });

            _rows.Add(Row("Le Chimiste d'Albuquerque", "CA"));
            _rows.Add(Row("Breaking-Bad", "FR"));
            _rows.Add(Row("Chimie mortelle", "CA"));

            Subject.BuildRequest(_series).Titles.Select(t => t.Title).Should().Equal("Chimie mortelle");
        }

        [Test]
        public void should_dedupe_dataset_rows_by_normalised_title_first_wins()
        {
            _rows.Add(Row("Chimie mortelle", "CA"));
            _rows.Add(Row("Chimie Mortelle!", "FR"));

            var titles = Subject.BuildRequest(_series).Titles;

            titles.Should().HaveCount(1);
            titles[0].Title.Should().Be("Chimie mortelle");
            titles[0].Region.Should().Be("CA");
        }

        [Test]
        public void should_import_one_request_per_series_with_candidates()
        {
            _rows.Add(Row("Chimie mortelle", "CA"));

            var summary = Subject.SyncSeries(_series);

            summary.SeriesChecked.Should().Be(1);
            summary.SeriesWithCandidates.Should().Be(1);
            summary.Result.TitlesAdded.Should().Be(1);
            Requests().Single().Titles.Single().Title.Should().Be("Chimie mortelle");
        }

        [Test]
        public void should_not_call_importer_when_nothing_is_missing()
        {
            _rows.Add(Row("Breaking Bad", "CA"));

            Subject.SyncSeries(_series);

            Mocker.GetMock<IUserSceneMappingImportService>().Verify(s => s.Import(It.IsAny<List<UserSceneMappingImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(false);
            _rows.Add(Row("Chimie mortelle", "CA"));

            Subject.SyncSeries(_series);
            Subject.SyncAll();

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.GetTitles(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IUserSceneMappingImportService>().Verify(s => s.Import(It.IsAny<List<UserSceneMappingImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_index_is_missing()
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(false);
            _rows.Add(Row("Chimie mortelle", "CA"));

            Subject.SyncSeries(_series);

            Mocker.GetMock<IUserSceneMappingImportService>().Verify(s => s.Import(It.IsAny<List<UserSceneMappingImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_skip_series_without_imdb_id()
        {
            _series.ImdbId = null;

            Subject.SyncSeries(_series);
            Subject.SyncAll().SeriesChecked.Should().Be(0);

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.GetTitles(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_sync_all_library_series_with_an_imdb_id()
        {
            var other = new Series { Id = 2, TvdbId = 73255, ImdbId = "tt0386676", Title = "The Office", Year = 2005 };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { _series, other });
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetTitles("tt0386676")).Returns(new List<ImdbAkasRow> { new ImdbAkasRow { Tconst = "tt0386676", Title = "Le Bureau", Region = "CA" } });
            Mocker.GetMock<ISceneMappingService>().Setup(s => s.FindByTvdbId(73255)).Returns(new List<SceneMapping>());

            _rows.Add(Row("Breaking Bad", "CA"));

            var summary = Subject.SyncAll();

            summary.SeriesChecked.Should().Be(2);
            summary.SeriesWithCandidates.Should().Be(1);
            Requests().Single().TvdbId.Should().Be(73255);
        }

        [Test]
        public void should_sync_on_series_added_event()
        {
            _rows.Add(Row("Chimie mortelle", "CA"));

            Subject.HandleAsync(new SeriesAddedEvent(_series));

            Requests().Should().HaveCount(1);
        }

        [Test]
        public void should_sync_on_series_updated_event()
        {
            _rows.Add(Row("Chimie mortelle", "CA"));

            Subject.HandleAsync(new SeriesUpdatedEvent(_series));

            Requests().Should().HaveCount(1);
        }

        [Test]
        public void should_swallow_errors_raised_while_handling_events()
        {
            _rows.Add(Row("Chimie mortelle", "CA"));

            Mocker.GetMock<IUserSceneMappingImportService>()
                  .Setup(s => s.Import(It.IsAny<List<UserSceneMappingImportRequest>>()))
                  .Throws(new InvalidOperationException("boom"));

            Subject.HandleAsync(new SeriesAddedEvent(_series));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
