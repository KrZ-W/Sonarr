using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class WhisperAsrAudioLanguageProbeFixture : CoreTest<WhisperAsrAudioLanguageProbe>
    {
        private const string Path = "/downloads/Series.S01E01.FRENCH.1080p-GRP/episode.mkv";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationEndpoint).Returns("http://whisper:9000");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationTimeout).Returns(120);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationClipOffset).Returns(300);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationClipLength).Returns(30);

            Mocker.GetMock<IAudioClipExtractor>()
                  .Setup(e => e.Extract(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                  .Returns(new byte[] { 1, 2, 3 });
        }

        private void GivenReply(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), json, status));
        }

        [Test]
        public void should_post_the_clip_and_map_the_reply()
        {
            HttpRequest sent = null;
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => sent = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "{\"detected_language\":\"french\",\"language_code\":\"fr\",\"confidence\":0.9731}"));

            var result = Subject.Probe(Path, 1, TimeSpan.FromHours(1.5));

            result.Should().NotBeNull();
            result.LanguageCode.Should().Be("fr");
            result.Language.Should().Be(Language.French);
            result.Confidence.Should().BeApproximately(0.9731, 0.0001);

            sent.Url.FullUri.Should().Be("http://whisper:9000/detect-language");
            sent.Method.Should().Be(System.Net.Http.HttpMethod.Post);
            sent.RequestTimeout.Should().Be(TimeSpan.FromSeconds(120));
            sent.Headers.ContentType.Should().StartWith("multipart/form-data");

            Mocker.GetMock<IAudioClipExtractor>()
                  .Verify(e => e.Extract(Path, 1, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)), Times.Once());
        }

        [Test]
        public void should_sample_from_the_middle_of_a_short_file()
        {
            GivenReply("{\"language_code\":\"fr\",\"confidence\":0.9}");

            Subject.Probe(Path, 0, TimeSpan.FromSeconds(200));

            Mocker.GetMock<IAudioClipExtractor>()
                  .Verify(e => e.Extract(Path, 0, TimeSpan.FromSeconds(85), TimeSpan.FromSeconds(30), It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public void should_return_null_language_for_codes_radarr_does_not_know()
        {
            GivenReply("{\"language_code\":\"haw\",\"confidence\":0.6}");

            var result = Subject.Probe(Path, 0, null);

            result.LanguageCode.Should().Be("haw");
            result.Language.Should().BeNull();
        }

        [Test]
        public void should_return_null_and_warn_once_when_the_clip_cannot_be_extracted()
        {
            Mocker.GetMock<IAudioClipExtractor>()
                  .Setup(e => e.Extract(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                  .Returns((byte[])null);

            Subject.Probe(Path, 0, null).Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(c => c.Post(It.IsAny<HttpRequest>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_once_when_the_endpoint_is_down()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("connection refused"));

            Subject.Probe(Path, 0, null).Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_once_on_http_error()
        {
            GivenReply("Internal Server Error", HttpStatusCode.InternalServerError);

            Subject.Probe(Path, 0, null).Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_once_on_unparsable_reply()
        {
            GivenReply("<html>nope</html>");

            Subject.Probe(Path, 0, null).Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_once_when_the_reply_has_no_language()
        {
            GivenReply("{\"confidence\":0.5}");

            Subject.Probe(Path, 0, null).Should().BeNull();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_null_and_warn_without_endpoint()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationEndpoint).Returns(string.Empty);

            Subject.Probe(Path, 0, null).Should().BeNull();

            Mocker.GetMock<IAudioClipExtractor>()
                  .Verify(e => e.Extract(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()), Times.Never());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(5400, 300, 30, 300)]
        [TestCase(330, 300, 30, 300)]
        [TestCase(200, 300, 30, 85)]
        [TestCase(20, 300, 30, 0)]
        [TestCase(0, 300, 30, 300)]
        public void clip_offset_should_fall_back_to_the_middle(int runtimeSeconds, int offsetSeconds, int lengthSeconds, int expectedSeconds)
        {
            TimeSpan? runtime = runtimeSeconds > 0 ? TimeSpan.FromSeconds(runtimeSeconds) : null;

            WhisperAsrAudioLanguageProbe.ClipOffset(TimeSpan.FromSeconds(offsetSeconds), TimeSpan.FromSeconds(lengthSeconds), runtime)
                .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
        }
    }
}
