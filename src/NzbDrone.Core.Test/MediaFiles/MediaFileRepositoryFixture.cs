using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, EpisodeFile>
    {
        private Series _series1;
        private Series _series2;

        [SetUp]
        public void Setup()
        {
            _series1 = Builder<Series>.CreateNew()
                                      .With(s => s.Id = 7)
                                      .Build();

            _series2 = Builder<Series>.CreateNew()
                                      .With(s => s.Id = 8)
                                      .Build();
        }

        [Test]
        public void get_files_by_series()
        {
            var files = Builder<EpisodeFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Languages = new List<Language> { Language.English })
                .With(c => c.Quality = new QualityModel(Quality.Bluray720p))
                .Random(4)
                .With(s => s.SeriesId = 12)
                .BuildListOfNew();

            Db.InsertMany(files);

            var seriesFiles = Subject.GetFilesBySeries(12);

            seriesFiles.Should().HaveCount(4);
            seriesFiles.Should().OnlyContain(c => c.SeriesId == 12);
        }

        [Test]
        public void should_delete_files_by_seriesId()
        {
            var items = Builder<EpisodeFile>.CreateListOfSize(5)
                .TheFirst(1)
                .With(c => c.SeriesId = _series2.Id)
                .TheRest()
                .With(c => c.SeriesId = _series1.Id)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.Bluray1080p))
                .With(c => c.Languages = new List<Language> { Language.English })
                .BuildListOfNew();

            Db.InsertMany(items);

            Subject.DeleteForSeries(new List<int> { _series1.Id });

            var removedItems = Subject.GetFilesBySeries(_series1.Id);
            var nonRemovedItems = Subject.GetFilesBySeries(_series2.Id);

            removedItems.Should().HaveCount(0);
            nonRemovedItems.Should().HaveCount(1);
        }

        // krzw(audio-language-verification)
        [Test]
        public void should_round_trip_audio_language_verification()
        {
            var file = Builder<EpisodeFile>.CreateNew()
                .With(c => c.Id = 0)
                .With(c => c.SeriesId = 12)
                .With(c => c.Quality = new QualityModel(Quality.Bluray720p))
                .With(c => c.Languages = new List<Language> { Language.French })
                .With(c => c.AudioLanguageVerification = new List<AudioLanguageVerification>
                {
                    new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "eng", DetectedLanguage = "fr", Confidence = 0.97, ProbedAt = new System.DateTime(2026, 9, 9, 12, 0, 0, System.DateTimeKind.Utc) }
                })
                .Build();

            Subject.Insert(file);

            var stored = Subject.All().Single();

            stored.AudioLanguageVerification.Should().HaveCount(1);
            stored.AudioLanguageVerification[0].StreamIndex.Should().Be(1);
            stored.AudioLanguageVerification[0].TaggedLanguage.Should().Be("eng");
            stored.AudioLanguageVerification[0].DetectedLanguage.Should().Be("fr");
            stored.AudioLanguageVerification[0].Confidence.Should().Be(0.97);
            stored.AudioLanguageVerification[0].Source.Should().Be("whisper");
            stored.AudioLanguageVerification[0].ProbedAt.Should().Be(new System.DateTime(2026, 9, 9, 12, 0, 0, System.DateTimeKind.Utc));
        }

        [Test]
        public void should_round_trip_null_audio_language_verification()
        {
            var file = Builder<EpisodeFile>.CreateNew()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.Bluray720p))
                .With(c => c.Languages = new List<Language> { Language.French })
                .With(c => c.AudioLanguageVerification = null)
                .Build();

            Subject.Insert(file);

            Subject.All().Single().AudioLanguageVerification.Should().BeNull();
        }
    }
}
