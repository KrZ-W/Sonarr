using System;
using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Tv.ImdbTitles;

namespace NzbDrone.Core.HealthCheck.Checks
{
    // krzw(imdb-title-provider): warns when the feature is on but its index is missing or stale
    // (older than twice the refresh interval, i.e. at least one scheduled refresh failed).
    [CheckOn(typeof(ConfigSavedEvent))]
    [CheckOn(typeof(ImdbTitleDatasetRefreshedEvent))]
    public class ImdbTitleDatasetCheck : HealthCheckBase
    {
        private readonly IConfigService _configService;
        private readonly IImdbAkasDatabase _database;

        public ImdbTitleDatasetCheck(IConfigService configService, IImdbAkasDatabase database, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _database = database;
        }

        public override HealthCheck Check()
        {
            if (!_configService.ImdbTitleProviderEnabled)
            {
                return new HealthCheck(GetType());
            }

            var info = _database.Exists ? _database.GetInfo() : null;

            if (info == null)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    _localizationService.GetLocalizedString("ImdbTitleDatasetMissingHealthCheckMessage"),
                    "#imdb-title-dataset-missing");
            }

            var intervalDays = Math.Max(1, _configService.ImdbTitleProviderRefreshInterval);
            var age = DateTime.UtcNow - (info.BuiltAt ?? DateTime.MinValue);

            if (age > TimeSpan.FromDays(intervalDays * 2))
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    _localizationService.GetLocalizedString("ImdbTitleDatasetStaleHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "days", (int)age.TotalDays },
                        { "interval", intervalDays }
                    }),
                    "#imdb-title-dataset-stale");
            }

            return new HealthCheck(GetType());
        }
    }
}
