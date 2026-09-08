using System.Collections.Generic;
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
    public class SceneMappingServiceUpsertReasonsFixture : CoreTest<SceneMappingService>
    {
        private Series _series;
        private List<SceneMapping> _existing;

        [SetUp]
        public void Setup()
        {
            _series = new Series { TvdbId = 100, Title = "Ma Série", CleanTitle = "Ma Série".CleanSeriesTitle() };
            _existing = new List<SceneMapping>();

            Mocker.SetConstant<IEnumerable<ISceneMappingProvider>>(new List<ISceneMappingProvider>());
            Mocker.GetMock<ISceneMappingRepository>().Setup(r => r.All()).Returns(_existing);
        }

        private UserSceneMappingUpsertResult Upsert(params string[] titles)
        {
            var mappings = new List<SceneMapping>();

            foreach (var title in titles)
            {
                mappings.Add(new SceneMapping { Title = title });
            }

            return Subject.UpsertUserMappingsDetailed(mappings, _series);
        }

        [Test]
        public void should_count_added_titles_not_rows()
        {
            // Folded spelling differs, so this one title stores two rows.
            var result = Upsert("Cœur d'Hiver");

            result.Added.Should().HaveCount(2);
            result.TitlesAdded.Should().Be(1);
            result.TitlesUnsearchable.Should().Be(0);
            result.TitlesConflictingMapping.Should().Be(0);
            result.TitlesAlreadyPresent.Should().Be(0);
        }

        [Test]
        public void should_count_unsearchable_title()
        {
            var result = Upsert("Медвежонок");

            result.TitlesUnsearchable.Should().Be(1);
            result.Added.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_count_own_title_as_already_present()
        {
            Upsert("Ma Série").TitlesAlreadyPresent.Should().Be(1);
        }

        [Test]
        public void should_count_conflict_with_another_series_mapping()
        {
            _existing.Add(new SceneMapping { TvdbId = 200, ParseTerm = "Un Titre".CleanSeriesTitle() });

            var result = Upsert("Un Titre");

            result.TitlesConflictingMapping.Should().Be(1);
            result.Added.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_count_fully_mapped_title_as_already_present()
        {
            _existing.Add(new SceneMapping { TvdbId = 100, ParseTerm = "Un Titre".CleanSeriesTitle() });

            var result = Upsert("Un Titre");

            result.TitlesAlreadyPresent.Should().Be(1);
            result.Added.Should().BeEmpty();
        }

        [Test]
        public void legacy_upsert_should_return_the_added_rows()
        {
            Subject.UpsertUserMappings(new List<SceneMapping> { new SceneMapping { Title = "Un Titre" } }, _series).Should().HaveCount(1);

            Mocker.GetMock<ISceneMappingRepository>().Verify(r => r.InsertMany(It.Is<List<SceneMapping>>(l => l.Count == 1)), Times.Once());
        }
    }
}
