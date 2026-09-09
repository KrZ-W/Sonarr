using System.Linq;
using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tv.ImdbTitles;
using Sonarr.Http;

namespace Sonarr.Api.V3.Config
{
    // krzw(imdb-title-provider): new endpoint, GET/PUT /api/v3/config/metadata
    [V3ApiController("config/metadata")]
    public class MetadataConfigController : ConfigController<MetadataConfigResource>
    {
        public MetadataConfigController(IConfigService configService)
            : base(configService)
        {
            // krzw(imdb-title-provider): validate the provider settings on save
            SharedValidator.RuleFor(c => c.ImdbTitleProviderRefreshInterval).GreaterThanOrEqualTo(1);
            SharedValidator.RuleFor(c => c.ImdbTitleProviderRegions)
                           .Must(BeTwoLetterCodes)
                           .WithMessage("Must be a comma-separated list of two-letter ISO 3166-1 region codes")
                           .When(c => c.ImdbTitleProviderEnabled);
            SharedValidator.RuleFor(c => c.ImdbTitleProviderLanguages)
                           .Must(BeTwoLetterCodes)
                           .WithMessage("Must be a comma-separated list of two-letter ISO 639-1 language codes")
                           .When(c => c.ImdbTitleProviderEnabled);
            SharedValidator.RuleFor(c => c.ImdbTitleProviderRegions)
                           .Must((c, regions) => ImdbAkasFilter.ParseList(regions).Any() || ImdbAkasFilter.ParseList(c.ImdbTitleProviderLanguages).Any())
                           .WithMessage("At least one region or language is required")
                           .When(c => c.ImdbTitleProviderEnabled);
        }

        protected override MetadataConfigResource ToResource(IConfigService model)
        {
            return MetadataConfigResourceMapper.ToResource(model);
        }

        private static bool BeTwoLetterCodes(string value)
        {
            return ImdbAkasFilter.ParseList(value).All(code => code.Length == 2 && code.All(char.IsLetter));
        }
    }
}
