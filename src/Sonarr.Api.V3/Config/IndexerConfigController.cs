using FluentValidation;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;  // krzw(indexer-cooldown)
using Sonarr.Http;
using Sonarr.Http.Validation;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/indexer")]
    public class IndexerConfigController : ConfigController<IndexerConfigResource>
    {
        public IndexerConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.MinimumAge)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.Retention)
                           .GreaterThanOrEqualTo(0);

            SharedValidator.RuleFor(c => c.RssSyncInterval)
                           .IsValidRssSyncInterval();

            // krzw(indexer-cooldown): reject input the status service would otherwise silently ignore
            SharedValidator.RuleFor(c => c.IndexerCooldownPeriods)
                           .Must(IndexerCooldownPeriods.IsValid)
                           .WithMessage("Must be a comma-separated list of whole minutes (e.g. 0,2,10,30,120), or blank for the default");
        }

        protected override IndexerConfigResource ToResource(IConfigService model)
        {
            return IndexerConfigResourceMapper.ToResource(model);
        }
    }
}
