using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DataAugmentation.Scene
{
    [TestFixture]
    public class UserSceneMappingImportServiceFixture : CoreTest<UserSceneMappingImportService>
    {
        private Series _series;
        private Series _other;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 1, TvdbId = 100, Title = "Ma Série", CleanTitle = "Ma Série".CleanSeriesTitle() };
            _other = new Series { Id = 2, TvdbId = 200, Title = "Coeur d'Hiver", CleanTitle = "Coeur d'Hiver".CleanSeriesTitle() };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { _series, _other });
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(100)).Returns(_series);
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(200)).Returns(_other);

            // Upsert reports every candidate as added unless a test says otherwise.
            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.UpsertUserMappingsDetailed(It.IsAny<List<SceneMapping>>(), It.IsAny<Series>()))
                  .Returns((List<SceneMapping> m, Series s) =>
                  {
                      var r = new UserSceneMappingUpsertResult();
                      r.Added.AddRange(m.Select(x => new SceneMapping { Title = x.Title, ParseTerm = x.Title.CleanSeriesTitle(), TvdbId = s.TvdbId }));
                      return r;
                  });
        }

        private static UserSceneMappingImportRequest Request(int tvdbId, params string[] titles)
        {
            return new UserSceneMappingImportRequest
            {
                TvdbId = tvdbId,
                SeriesTitle = "Ma Série",
                Year = 2020,
                Titles = titles.Select(t => new UserSceneMappingImportEntry { Title = t, Region = "CA" }).ToList()
            };
        }

        [Test]
        public void should_return_empty_result_for_null_or_empty_input()
        {
            Subject.Import(null).SeriesProcessed.Should().Be(0);
            Subject.Import(new List<UserSceneMappingImportRequest>()).SeriesProcessed.Should().Be(0);

            Mocker.GetMock<ISeriesService>().Verify(s => s.GetAllSeries(), Times.Never());
        }

        [Test]
        public void should_report_series_not_found_when_neither_id_resolves()
        {
            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(999, "Titre") });

            result.SeriesProcessed.Should().Be(0);
            result.SeriesNotFound.Should().ContainSingle().Which.Should().Be("Ma Série (2020) [tvdb:999]");
        }

        [Test]
        public void should_fall_back_to_imdb_id()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByImdbId("tt0000001")).Returns(_series);

            var request = Request(999, "Titre");
            request.ImdbId = "tt0000001";

            var result = Subject.Import(new List<UserSceneMappingImportRequest> { request });

            result.SeriesProcessed.Should().Be(1);
            result.TitlesAdded.Should().Be(1);
        }

        [Test]
        public void should_not_look_up_tvdb_id_zero()
        {
            Subject.Import(new List<UserSceneMappingImportRequest> { Request(0, "Titre") }).SeriesNotFound.Should().HaveCount(1);

            Mocker.GetMock<ISeriesService>().Verify(s => s.FindByTvdbId(0), Times.Never());
        }

        [Test]
        public void should_drop_blank_titles_and_map_region_to_comment()
        {
            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "Titre", "", "  ", null) });

            result.TitlesAdded.Should().Be(1);
            result.TitlesSkipped.Should().Be(0);
            Mocker.GetMock<ISceneMappingService>().Verify(s => s.UpsertUserMappingsDetailed(It.Is<List<SceneMapping>>(l => l.Count == 1 && l[0].Title == "Titre" && l[0].Comment == "CA"), _series), Times.Once());
        }

        [Test]
        public void should_guard_a_title_that_is_another_library_series()
        {
            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "Coeur d'Hiver", "Titre") });

            result.TitlesGuardedLibrary.Should().Be(1);
            result.TitlesAdded.Should().Be(1);
            result.TitlesSkipped.Should().Be(1);
            Mocker.GetMock<ISceneMappingService>().Verify(s => s.UpsertUserMappingsDetailed(It.Is<List<SceneMapping>>(l => l.Count == 1 && l[0].Title == "Titre"), _series), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_guard_by_folded_spelling_too()
        {
            // "Cœur d’Hiver" folds to "Coeur d'Hiver", which is another library series' title.
            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "Cœur d’Hiver") });

            result.TitlesGuardedLibrary.Should().Be(1);
            result.TitlesAdded.Should().Be(0);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_guard_the_series_own_title()
        {
            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "Ma Série") });

            result.TitlesGuardedLibrary.Should().Be(0);
        }

        [Test]
        public void should_pass_upsert_breakdown_through()
        {
            var upsert = new UserSceneMappingUpsertResult { TitlesUnsearchable = 1, TitlesConflictingMapping = 2, TitlesAlreadyPresent = 3 };
            upsert.Added.Add(new SceneMapping { Title = "A" });
            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.UpsertUserMappingsDetailed(It.IsAny<List<SceneMapping>>(), It.IsAny<Series>()))
                  .Returns(upsert);

            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "A", "B", "C", "D", "E", "F", "G") });

            result.TitlesAdded.Should().Be(1);
            result.TitlesUnsearchable.Should().Be(1);
            result.TitlesConflictingMapping.Should().Be(2);
            result.TitlesAlreadyPresent.Should().Be(3);
            result.TitlesSkipped.Should().Be(6);
        }

        [Test]
        public void should_report_a_failed_series_and_continue_with_the_next()
        {
            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.UpsertUserMappingsDetailed(It.IsAny<List<SceneMapping>>(), _series))
                  .Throws(new InvalidOperationException("boom"));

            var result = Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "Titre"), Request(200, "Autre") });

            result.SeriesFailed.Should().ContainSingle().Which.Should().Be("Ma Série (2020) [tvdb:100]: boom");
            result.SeriesProcessed.Should().Be(1);
            result.TitlesAdded.Should().Be(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_build_the_library_lookup_once_per_request()
        {
            Subject.Import(new List<UserSceneMappingImportRequest> { Request(100, "A"), Request(200, "B"), Request(999, "C") });

            Mocker.GetMock<ISeriesService>().Verify(s => s.GetAllSeries(), Times.Once());
        }
    }
}
