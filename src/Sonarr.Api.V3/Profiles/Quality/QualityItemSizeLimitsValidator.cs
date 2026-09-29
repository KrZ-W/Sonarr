using System.Collections.Generic;
using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace Sonarr.Api.V3.Profiles.Quality
{
    // krzw(profile-size-limits)
    // Validates the per-item size overrides: each value within the global limits, and the
    // EFFECTIVE triple (override ?? group ?? global definition) ordered min <= preferred <= max.
    // MaxSize 0 means unlimited and is excluded from the ordering check.
    public class QualityItemSizeLimitsValidator<T> : PropertyValidator
    {
        private readonly IQualityDefinitionService _qualityDefinitionService;

        public QualityItemSizeLimitsValidator(IQualityDefinitionService qualityDefinitionService)
        {
            _qualityDefinitionService = qualityDefinitionService;
        }

        protected override string GetDefaultMessageTemplate() => "{Message}";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue is not IList<QualityProfileQualityItemResource> items)
            {
                return true;
            }

            foreach (var item in items)
            {
                if (!ValuesInRange(item, out var rangeMessage))
                {
                    context.MessageFormatter.AppendArgument("Message", rangeMessage);
                    return false;
                }

                foreach (var member in item.Items)
                {
                    if (!ValuesInRange(member, out var memberRangeMessage))
                    {
                        context.MessageFormatter.AppendArgument("Message", memberRangeMessage);
                        return false;
                    }
                }
            }

            var profile = new QualityProfile { Items = items.Select(i => i.ToModel()).ToList() };

            foreach (var quality in profile.Items.SelectMany(i => i.GetQualities()).Where(q => q != null))
            {
                var definition = _qualityDefinitionService.Get(quality);
                var effective = QualityProfileSizeLimits.Resolve(profile, quality, definition);
                var (group, member) = QualityProfileSizeLimits.FindItems(profile, quality);

                if (!QualityProfileSizeLimits.HasOverride(member) && !QualityProfileSizeLimits.HasOverride(group))
                {
                    continue;
                }

                if (effective.MinSize.HasValue && effective.PreferredSize.HasValue && effective.MinSize.Value > effective.PreferredSize.Value)
                {
                    context.MessageFormatter.AppendArgument("Message", $"{quality.Name}: effective minimum size ({effective.MinSize.Value}) must not exceed preferred size ({effective.PreferredSize.Value})");
                    return false;
                }

                if (!effective.IsMaxUnlimited)
                {
                    if (effective.PreferredSize.HasValue && effective.PreferredSize.Value > effective.MaxSize.Value)
                    {
                        context.MessageFormatter.AppendArgument("Message", $"{quality.Name}: effective preferred size ({effective.PreferredSize.Value}) must not exceed maximum size ({effective.MaxSize.Value})");
                        return false;
                    }

                    if (effective.MinSize.HasValue && effective.MinSize.Value > effective.MaxSize.Value)
                    {
                        context.MessageFormatter.AppendArgument("Message", $"{quality.Name}: effective minimum size ({effective.MinSize.Value}) must not exceed maximum size ({effective.MaxSize.Value})");
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValuesInRange(QualityProfileQualityItemResource item, out string message)
        {
            message = null;
            var label = item.Quality?.Name ?? item.Name;

            foreach (var (name, value) in new[] { ("minimum", item.MinSize), ("preferred", item.PreferredSize), ("maximum", item.MaxSize) })
            {
                if (value.HasValue && (value.Value < QualityDefinitionLimits.Min || value.Value > QualityDefinitionLimits.Max))
                {
                    message = $"{label}: {name} size override must be between {QualityDefinitionLimits.Min} and {QualityDefinitionLimits.Max} MB/min";
                    return false;
                }
            }

            return true;
        }
    }
}
