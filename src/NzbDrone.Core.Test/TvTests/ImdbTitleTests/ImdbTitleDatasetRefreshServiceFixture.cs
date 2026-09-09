using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv.ImdbTitles;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests.ImdbTitleTests
{
    [TestFixture]
    public class ImdbTitleDatasetRefreshServiceFixture : CoreTest<ImdbTitleDatasetRefreshService>
    {
        private const string Signature = "regions=CA,FR;languages=fr";

        private HttpRequest _request;
        private List<ImdbAkasRow> _built;

        [SetUp]
        public void Setup()
        {
            _request = null;
            _built = null;

            Mocker.GetMock<IAppFolderInfo>().SetupGet(f => f.AppDataFolder).Returns(TempFolder);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderRegions).Returns("CA,FR");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderLanguages).Returns("fr");

            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Path).Returns(Path.Combine(TempFolder, "imdb-akas.db"));
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(false);
            Mocker.GetMock<IImdbAkasDatabase>()
                  .Setup(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()))
                  .Returns((IEnumerable<ImdbAkasRow> rows, ImdbAkasDatabaseInfo info) =>
                  {
                      _built = rows.ToList();
                      Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
                      Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetInfo()).Returns(new ImdbAkasDatabaseInfo { ETag = info.ETag, RowCount = _built.Count, FilterSignature = info.FilterSignature });
                      return _built.Count;
                  });

            Mocker.GetMock<IImdbTitleSyncService>().Setup(s => s.SyncAll()).Returns(new ImdbTitleSyncSummary());
        }

        private void GivenExistingIndex(string etag, string signature = Signature)
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetInfo()).Returns(new ImdbAkasDatabaseInfo { ETag = etag, LastModified = "Mon, 01 Sep 2025 00:00:00 GMT", FilterSignature = signature, RowCount = 5 });
        }

        private void GivenServerResponds(HttpStatusCode status, string etag = null, byte[] body = null)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest request) =>
                  {
                      _request = request;

                      if (body != null)
                      {
                          request.ResponseStream.Write(body, 0, body.Length);
                      }

                      var headers = new HttpHeader();

                      if (etag != null)
                      {
                          headers.Add("ETag", etag);
                      }

                      return new HttpResponse(request, headers, System.Array.Empty<byte>(), status);
                  });
        }

        private void GivenServerFails()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Throws(new WebException("connection reset"));
        }

        [Test]
        public void should_do_nothing_when_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(false);

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Never());
        }

        [Test]
        public void should_download_index_and_sync_when_no_index_exists()
        {
            GivenServerResponds(HttpStatusCode.OK, "\"v1\"", ImdbAkasTsvParserFixture.Gzip(ImdbAkasTsvParserFixture.Sample));

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            _request.Url.ToString().Should().Be(ImdbTitleDatasetRefreshService.DatasetUrl);
            _request.Headers.ContainsKey("If-None-Match").Should().BeFalse();
            _built.Select(r => r.Title).Should().Equal("Le Fabuleux Destin d'Amélie Poulain", "Amélie de Montmartre", "Le destin fabuleux", "Le Parrain");

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.Is<ImdbAkasDatabaseInfo>(i => i.ETag == "\"v1\"" && i.FilterSignature == Signature)), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<ImdbTitleDatasetRefreshedEvent>(ev => ev.Downloaded && ev.RowCount == 4)), Times.Once());
            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Once());

            Directory.GetFiles(TempFolder, "*.download").Should().BeEmpty();
        }

        [Test]
        public void should_send_conditional_headers_and_skip_download_when_not_modified()
        {
            GivenExistingIndex("\"v1\"");
            GivenServerResponds(HttpStatusCode.NotModified);

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            _request.Headers.GetSingleValue("If-None-Match").Should().Be("\"v1\"");
            _request.Headers.GetSingleValue("If-Modified-Since").Should().Be("Mon, 01 Sep 2025 00:00:00 GMT");

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<ImdbTitleDatasetRefreshedEvent>(ev => !ev.Downloaded)), Times.Once());
            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Once());
        }

        [Test]
        public void should_skip_rebuild_when_server_ignores_conditional_request_but_etag_matches()
        {
            GivenExistingIndex("\"v1\"");
            GivenServerResponds(HttpStatusCode.OK, "\"v1\"", ImdbAkasTsvParserFixture.Gzip(ImdbAkasTsvParserFixture.Sample));

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()), Times.Never());
        }

        [Test]
        public void should_download_unconditionally_when_filter_changed()
        {
            GivenExistingIndex("\"v1\"", "regions=FR;languages=fr");
            GivenServerResponds(HttpStatusCode.OK, "\"v1\"", ImdbAkasTsvParserFixture.Gzip(ImdbAkasTsvParserFixture.Sample));

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            _request.Headers.ContainsKey("If-None-Match").Should().BeFalse();
            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()), Times.Once());
        }

        [Test]
        public void should_keep_previous_index_and_still_sync_when_download_fails()
        {
            GivenExistingIndex("\"v1\"");
            GivenServerFails();

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()), Times.Never());
            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_sync_when_first_download_fails()
        {
            GivenServerFails();

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<ImdbTitleDatasetRefreshedEvent>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_keep_previous_index_when_dump_is_corrupt()
        {
            GivenExistingIndex("\"v1\"");
            GivenServerResponds(HttpStatusCode.OK, "\"v2\"", new byte[] { 1, 2, 3, 4 });

            Mocker.GetMock<IImdbAkasDatabase>()
                  .Setup(d => d.Build(It.IsAny<IEnumerable<ImdbAkasRow>>(), It.IsAny<ImdbAkasDatabaseInfo>()))
                  .Returns((IEnumerable<ImdbAkasRow> rows, ImdbAkasDatabaseInfo info) => rows.Count());

            Subject.Execute(new ImdbTitleDatasetRefreshCommand());

            Mocker.GetMock<IImdbTitleSyncService>().Verify(s => s.SyncAll(), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
