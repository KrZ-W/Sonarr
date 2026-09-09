using System;
using System.IO;
using System.Net;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): the scheduled task. Downloads IMDb's title.akas dump to a temp file
    // (conditional on ETag / Last-Modified so an unchanged dump is not re-fetched), streams it into
    // the SQLite index, then applies the index to every library series. The dump is consumed at
    // runtime only and never redistributed; see docs/features/imdb-title-provider.md for the
    // dataset licence.
    public class ImdbTitleDatasetRefreshService : IExecute<ImdbTitleDatasetRefreshCommand>
    {
        public const string DatasetUrl = "https://datasets.imdbws.com/title.akas.tsv.gz";

        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly IImdbAkasDatabase _database;
        private readonly IImdbTitleSyncService _syncService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ImdbTitleDatasetRefreshService(IConfigService configService,
                                              IHttpClient httpClient,
                                              IImdbAkasDatabase database,
                                              IImdbTitleSyncService syncService,
                                              IAppFolderInfo appFolderInfo,
                                              IEventAggregator eventAggregator,
                                              Logger logger)
        {
            _configService = configService;
            _httpClient = httpClient;
            _database = database;
            _syncService = syncService;
            _appFolderInfo = appFolderInfo;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(ImdbTitleDatasetRefreshCommand message)
        {
            if (!_configService.ImdbTitleProviderEnabled)
            {
                _logger.Info("IMDb Title Provider is disabled; skipping dataset refresh");
                return;
            }

            var filter = ImdbAkasFilter.FromConfig(_configService);

            if (filter.Regions.Count == 0 && filter.Languages.Count == 0)
            {
                _logger.Warn("IMDb Title Provider has no regions and no languages configured; nothing to keep from the dataset");
                return;
            }

            var downloaded = Refresh(filter);
            var info = _database.GetInfo();
            var rowCount = info?.RowCount ?? 0;

            if (!_database.Exists)
            {
                return;
            }

            _eventAggregator.PublishEvent(new ImdbTitleDatasetRefreshedEvent(downloaded, rowCount));

            var summary = _syncService.SyncAll();

            _logger.Info("IMDb titles applied to library: {0}", summary);
        }

        // Returns true when a new dump was downloaded and indexed.
        private bool Refresh(ImdbAkasFilter filter)
        {
            var current = _database.Exists ? _database.GetInfo() : null;
            var conditional = current != null && current.FilterSignature == filter.Signature;

            var tempPath = Path.Combine(_appFolderInfo.GetAppDataPath(), ImdbAkasDatabase.FileName + ".download");

            try
            {
                var request = new HttpRequest(DatasetUrl);
                request.AllowAutoRedirect = true;
                request.RequestTimeout = TimeSpan.FromMinutes(30);
                request.LogResponseContent = false;

                if (conditional)
                {
                    if (current.ETag.IsNotNullOrWhiteSpace())
                    {
                        request.Headers.Add("If-None-Match", current.ETag);
                    }

                    if (current.LastModified.IsNotNullOrWhiteSpace())
                    {
                        request.Headers.Add("If-Modified-Since", current.LastModified);
                    }
                }

                _logger.Info("Downloading IMDb akas dataset from {0}{1}", DatasetUrl, conditional ? " (conditional)" : string.Empty);

                HttpResponse response;

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite))
                {
                    request.ResponseStream = stream;
                    response = _httpClient.Get(request);
                }

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    _logger.Info("IMDb akas dataset unchanged since the last refresh (ETag {0}); keeping the existing index of {1} rows", current.ETag, current.RowCount);
                    return false;
                }

                var etag = response.Headers.GetSingleValue("ETag");
                var lastModified = response.Headers.GetSingleValue("Last-Modified");

                if (conditional && etag.IsNotNullOrWhiteSpace() && etag == current.ETag)
                {
                    _logger.Info("IMDb akas dataset ETag {0} unchanged; keeping the existing index of {1} rows", etag, current.RowCount);
                    return false;
                }

                var stats = new ImdbAkasParseStats();
                var info = new ImdbAkasDatabaseInfo
                {
                    ETag = etag,
                    LastModified = lastModified,
                    FilterSignature = filter.Signature
                };

                _logger.Info("Indexing IMDb akas rows for regions [{0}] / languages [{1}]", string.Join(",", filter.Regions), string.Join(",", filter.Languages));

                var kept = _database.Build(ImdbAkasTsvParser.ParseGzipFile(tempPath, filter, stats), info);

                _logger.Info("IMDb akas dataset indexed: scanned {0} rows, kept {1} -> {2}", stats.Scanned, kept, _database.Path);

                return true;
            }
            catch (Exception ex)
            {
                if (current != null)
                {
                    _logger.Error(ex, "IMDb akas dataset refresh failed; keeping the previous index of {0} rows", current.RowCount);
                }
                else
                {
                    _logger.Error(ex, "IMDb akas dataset refresh failed and no previous index exists");
                }

                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not delete temporary download {0}", tempPath);
                }
            }
        }
    }
}
