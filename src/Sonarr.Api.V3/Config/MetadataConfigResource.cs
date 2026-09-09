using NzbDrone.Core.Configuration;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Config
{
    // krzw(imdb-title-provider): Sonarr has no metadata config endpoint upstream; this one carries
    // the IMDb Title Provider settings (Settings -> Metadata).
    public class MetadataConfigResource : RestResource
    {
        public bool ImdbTitleProviderEnabled { get; set; }
        public string ImdbTitleProviderRegions { get; set; }
        public string ImdbTitleProviderLanguages { get; set; }
        public int ImdbTitleProviderRefreshInterval { get; set; }
    }

    public static class MetadataConfigResourceMapper
    {
        public static MetadataConfigResource ToResource(IConfigService model)
        {
            return new MetadataConfigResource
            {
                ImdbTitleProviderEnabled = model.ImdbTitleProviderEnabled,
                ImdbTitleProviderRegions = model.ImdbTitleProviderRegions,
                ImdbTitleProviderLanguages = model.ImdbTitleProviderLanguages,
                ImdbTitleProviderRefreshInterval = model.ImdbTitleProviderRefreshInterval,
            };
        }
    }
}
