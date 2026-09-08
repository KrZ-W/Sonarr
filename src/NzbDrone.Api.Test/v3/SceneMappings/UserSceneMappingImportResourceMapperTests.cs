using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using Sonarr.Api.V3.SceneMappings;
using Sonarr.Http.REST;

namespace NzbDrone.Api.Test.v3.SceneMappings
{
    [Parallelizable(ParallelScope.All)]
    public class UserSceneMappingImportResourceMapperTests
    {
        private static UserSceneMappingImportResource Row(int tvdbId, params UserSceneMappingImportEntryResource[] titles)
        {
            return new UserSceneMappingImportResource
            {
                TvdbId = tvdbId,
                ImdbId = "tt0000001",
                SeriesTitle = "Ma Série",
                Year = 2020,
                MissingFrenchTitles = titles.ToList()
            };
        }

        [Test]
        public void Null_body_maps_to_empty_request_list()
        {
            ((List<UserSceneMappingImportResource>)null).ToImportRequests().Should().BeEmpty();
        }

        [Test]
        public void Maps_identity_fields_and_titles()
        {
            var request = new List<UserSceneMappingImportResource>
            {
                Row(100, new UserSceneMappingImportEntryResource { Title = "Ma Série Québécoise", Region = "CA" })
            }.ToImportRequests().Should().ContainSingle().Subject;

            request.TvdbId.Should().Be(100);
            request.ImdbId.Should().Be("tt0000001");
            request.SeriesTitle.Should().Be("Ma Série");
            request.Year.Should().Be(2020);
            request.Titles.Should().ContainSingle().Which.Should().BeEquivalentTo(new UserSceneMappingImportEntry { Title = "Ma Série Québécoise", Region = "CA" });
        }

        [Test]
        public void Titles_alias_feeds_the_same_list()
        {
            var resource = new UserSceneMappingImportResource { TvdbId = 100, Titles = new List<UserSceneMappingImportEntryResource> { new UserSceneMappingImportEntryResource { Title = "A" } } };

            resource.MissingFrenchTitles.Should().HaveCount(1);
            new List<UserSceneMappingImportResource> { resource }.ToImportRequests()[0].Titles.Should().HaveCount(1);
        }

        [Test]
        public void Rejects_too_many_series()
        {
            var rows = Enumerable.Range(1, UserSceneMappingImportResourceMapper.MaxSeriesPerRequest + 1).Select(i => Row(i)).ToList();

            var act = () => rows.ToImportRequests();

            act.Should().Throw<BadRequestException>();
        }

        [Test]
        public void Rejects_too_many_titles_for_one_series()
        {
            var titles = Enumerable.Range(1, UserSceneMappingImportResourceMapper.MaxTitlesPerSeries + 1).Select(i => new UserSceneMappingImportEntryResource { Title = $"T{i}" }).ToArray();

            var act = () => new List<UserSceneMappingImportResource> { Row(100, titles) }.ToImportRequests();

            act.Should().Throw<BadRequestException>();
        }

        [Test]
        public void Rejects_over_long_title()
        {
            var act = () => new List<UserSceneMappingImportResource> { Row(100, new UserSceneMappingImportEntryResource { Title = new string('x', UserSceneMappingImportResourceMapper.MaxTitleLength + 1) }) }.ToImportRequests();

            act.Should().Throw<BadRequestException>();
        }

        [Test]
        public void Result_maps_counts_and_lists()
        {
            var result = new UserSceneMappingImportResult { SeriesProcessed = 2, TitlesAdded = 3, TitlesGuardedLibrary = 1, TitlesConflictingMapping = 1, TitlesUnsearchable = 1, TitlesAlreadyPresent = 2 };
            result.SeriesNotFound.Add("nf");
            result.SeriesFailed.Add("f");

            var resource = result.ToResource();

            resource.SeriesProcessed.Should().Be(2);
            resource.TitlesAdded.Should().Be(3);
            resource.TitlesSkipped.Should().Be(5);
            resource.TitlesGuardedLibrary.Should().Be(1);
            resource.TitlesConflictingMapping.Should().Be(1);
            resource.TitlesUnsearchable.Should().Be(1);
            resource.TitlesAlreadyPresent.Should().Be(2);
            resource.SeriesNotFound.Should().Equal("nf");
            resource.SeriesFailed.Should().Equal("f");
        }
    }
}
