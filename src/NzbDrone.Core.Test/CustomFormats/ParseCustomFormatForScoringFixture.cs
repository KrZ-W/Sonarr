using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.CustomFormats
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class ParseCustomFormatForScoringFixture : CoreTest<CustomFormatCalculationService>
    {
        private const string SceneTitle = "Le.Sapin.a.les.boules.1989.MULTi.VFi.1080p.BluRay.x264-GROUP";
        private const string GrabTitle = "Le Sapin a les boules TRUEFRENCH HDLight 1080p 1989";

        private CustomFormat _sceneFormat;
        private CustomFormat _grabFormat;
        private Series _series;
        private EpisodeFile _episodeFile;

        [SetUp]
        public void Setup()
        {
            _sceneFormat = new CustomFormat("Scene Only", new ReleaseTitleSpecification { Value = "MULTi" }) { Id = 1 };
            _grabFormat = new CustomFormat("Grab Only", new ReleaseTitleSpecification { Value = "TRUEFRENCH" }) { Id = 2 };

            Mocker.GetMock<ICustomFormatService>()
                  .Setup(s => s.All())
                  .Returns(new List<CustomFormat> { _sceneFormat, _grabFormat });

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Title = "Le Sapin a les boules")
                                     .Build();

            _episodeFile = new EpisodeFile
            {
                SceneName = SceneTitle,
                GrabbedReleaseTitle = GrabTitle,
                RelativePath = "Season 01/episode.mkv",
                Quality = new QualityModel(Quality.HDTV720p),
                Languages = new List<Language> { Language.French }
            };

            GivenScores(sceneScore: 10, scenePriority: false, grabScore: 100, grabPriority: false);
            GivenSetting(true);
        }

        private void GivenSetting(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.ScoreFilesByGrabbedReleaseTitle)
                  .Returns(enabled);
        }

        private void GivenScores(int sceneScore, bool scenePriority, int grabScore, bool grabPriority)
        {
            _series.QualityProfile = new QualityProfile
            {
                FormatItems = new List<ProfileFormatItem>
                {
                    new ProfileFormatItem { Format = _sceneFormat, Score = sceneScore, Priority = scenePriority },
                    new ProfileFormatItem { Format = _grabFormat, Score = grabScore, Priority = grabPriority }
                }
            };
        }

        [Test]
        public void should_use_the_grabbed_release_title_when_it_scores_higher()
        {
            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _grabFormat });
        }

        [Test]
        public void should_keep_the_scene_name_when_the_grabbed_release_title_scores_lower()
        {
            GivenScores(sceneScore: 10, scenePriority: false, grabScore: 5, grabPriority: false);

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_keep_the_scene_name_when_the_grabbed_release_title_would_lower_the_priority_score()
        {
            // The grab title wins by a mile on total score but drops the priority custom format,
            // and priority is compared before quality, so it must not be selected.
            GivenScores(sceneScore: 10, scenePriority: true, grabScore: 1000, grabPriority: false);

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_keep_the_scene_name_when_the_setting_is_off()
        {
            GivenSetting(false);

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_keep_the_scene_name_when_there_is_no_grabbed_release_title()
        {
            _episodeFile.GrabbedReleaseTitle = null;

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_fall_back_to_the_legacy_ladder_when_the_series_has_no_quality_profile()
        {
            _series.QualityProfile = null;

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_consider_the_original_file_path_when_there_is_no_scene_name()
        {
            _episodeFile.SceneName = null;
            _episodeFile.OriginalFilePath = "Some.Folder/" + SceneTitle + ".mkv";

            GivenScores(sceneScore: 10, scenePriority: false, grabScore: 5, grabPriority: false);

            Subject.ParseCustomFormatForScoring(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_not_change_the_naming_ladder_when_the_setting_is_on()
        {
            // FileNameBuilder renders the {Custom Formats} token through this overload. If the
            // scoring rule leaked into it, enabling the setting would propose a library-wide rename.
            Subject.ParseCustomFormat(_episodeFile, _series)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }

        [Test]
        public void should_use_the_grabbed_release_title_for_a_local_episode()
        {
            var localEpisode = new LocalEpisode
            {
                Series = _series,
                Path = @"C:\Test\Season 01\episode.mkv".AsOsAgnostic(),
                SceneName = SceneTitle,
                GrabbedReleaseTitle = GrabTitle,
                Quality = new QualityModel(Quality.HDTV720p),
                Languages = new List<Language> { Language.French }
            };

            Subject.ParseCustomFormatForScoring(localEpisode)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _grabFormat });
        }

        [Test]
        public void should_keep_the_scene_name_for_a_local_episode_when_the_grab_scores_lower()
        {
            GivenScores(sceneScore: 10, scenePriority: false, grabScore: 5, grabPriority: false);

            var localEpisode = new LocalEpisode
            {
                Series = _series,
                Path = @"C:\Test\Season 01\episode.mkv".AsOsAgnostic(),
                SceneName = SceneTitle,
                GrabbedReleaseTitle = GrabTitle,
                Quality = new QualityModel(Quality.HDTV720p),
                Languages = new List<Language> { Language.French }
            };

            Subject.ParseCustomFormatForScoring(localEpisode)
                   .Should().BeEquivalentTo(new List<CustomFormat> { _sceneFormat });
        }
    }
}
