using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class FfprobeAudioTrackLayoutReaderFixture : CoreTest<FfprobeAudioTrackLayoutReader>
    {
        public const string StreamJson = @"{
  ""streams"": [
    { ""index"": 0, ""codec_name"": ""h264"", ""codec_type"": ""video"", ""width"": 1920, ""height"": 1080 },
    { ""index"": 1, ""codec_name"": ""eac3"", ""codec_type"": ""audio"", ""channels"": 6, ""tags"": { ""language"": ""eng"", ""title"": ""English 5.1"" } },
    { ""index"": 2, ""codec_name"": ""aac"", ""codec_type"": ""audio"", ""channels"": 2, ""tags"": { ""language"": ""und"" } },
    { ""index"": 3, ""codec_name"": ""subrip"", ""codec_type"": ""subtitle"", ""tags"": { ""language"": ""fre"" } }
  ],
  ""format"": { ""filename"": ""movie.mkv"", ""format_name"": ""matroska,webm"", ""duration"": ""5400.0"" }
}";

        [Test]
        public void should_return_empty_without_raw_stream_data()
        {
            Subject.Read(null).Should().BeEmpty();
            Subject.Read(new MediaInfoModel()).Should().BeEmpty();
        }

        [Test]
        public void should_return_empty_on_unparsable_json()
        {
            Subject.Read(new MediaInfoModel { RawStreamData = "not json" }).Should().BeEmpty();
        }

        [Test]
        public void should_list_audio_streams_with_audio_relative_indexes()
        {
            var tracks = Subject.Read(new MediaInfoModel { RawStreamData = StreamJson });

            tracks.Should().HaveCount(2);
            tracks[0].StreamIndex.Should().Be(0);
            tracks[0].Codec.Should().Be("eac3");
            tracks[0].Channels.Should().Be(6);
            tracks[0].Language.Should().Be("eng");
            tracks[0].Title.Should().Be("English 5.1");
            tracks[0].TaggedLanguage.Should().Be(Language.English);
            tracks[1].StreamIndex.Should().Be(1);
            tracks[1].Language.Should().Be("und");
            tracks[1].TaggedLanguage.Should().BeNull();
        }

        [Test]
        public void signature_should_be_ordered_codec_channels_language_title()
        {
            var tracks = Subject.Read(new MediaInfoModel { RawStreamData = StreamJson });

            AudioTrackLayout.Signature(tracks).Should().Be("eac3/6/eng/English 5.1|aac/2/und/");
        }
    }
}
