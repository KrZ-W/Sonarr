using System.Collections.Generic;
using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace Sonarr.Api.V3.Profiles.Quality
{
    // krzw(profile-size-limits)
    // Validates the per-item size overrides:
    //  - every override within the global QualityDefinitionLimits range;
    //  - values set on the SAME item ordered min <= preferred <= max (max 0 = unlimited is skipped);
    //  - the EFFECTIVE min/max window (override ?? group ?? global definition) of every quality that has
    //    an override must satisfy min <= max, with the message naming where each value came from.
    // The effective preferred size is not validated against inherited values: the resolver clamps it
    // into the effective window at runtime, so a profile that only caps max stays consistent even when
    // the global definitions change later.
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
                foreach (var candidate in new[] { item }.Concat(item.Items ?? new List<QualityProfileQualityItemResource>()))
                {
                    if (!ValidateItem(candidate, out var message))
                    {
                        context.MessageFormatter.AppendArgument("Message", message);
                        return false;
                    }
                }
            }

            var profile = new QualityProfile { Items = items.Select(i => i.ToModel()).ToList() };

            foreach (var quality in profile.Items.SelectMany(i => i.GetQualities()).Where(q => q != null))
            {
                var (group, member) = QualityProfileSizeLimits.FindItems(profile, quality);

                if (!QualityProfileSizeLimits.HasOverride(member) && !QualityProfileSizeLimits.HasOverride(group))
                {
                    continue;
                }

                var effective = QualityProfileSizeLimits.Resolve(profile, quality, _qualityDefinitionService.Get(quality));

                if (!effective.IsMaxUnlimited && effective.MinSize.HasValue && effective.MinSize.Value > effective.MaxSize.Value)
                {
                    context.MessageFormatter.AppendArgument("Message", $"{quality.Name}: minimum size {Describe(effective.MinSize.Value, effective.MinSizeSource, group)} must not exceed maximum size {Describe(effective.MaxSize.Value, effective.MaxSizeSource, group)}");
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateItem(QualityProfileQualityItemResource item, out string message)
        {
            message = null;
            var label = Label(item);

            foreach (var (name, value) in new[] { ("minimum", item.MinSize), ("preferred", item.PreferredSize), ("maximum", item.MaxSize) })
            {
                if (value.HasValue && (value.Value < QualityDefinitionLimits.Min || value.Value > QualityDefinitionLimits.Max))
                {
                    message = $"{label}: {name} size override must be between {QualityDefinitionLimits.Min} and {QualityDefinitionLimits.Max} MB/min";
                    return false;
                }
            }

            var maxUnlimited = !item.MaxSize.HasValue || item.MaxSize.Value == 0;

            if (item.MinSize.HasValue && item.PreferredSize.HasValue && item.MinSize.Value > item.PreferredSize.Value)
            {
                message = $"{label}: minimum size override ({item.MinSize.Value}) must not exceed preferred size override ({item.PreferredSize.Value})";
                return false;
            }

            if (!maxUnlimited && item.PreferredSize.HasValue && item.PreferredSize.Value > item.MaxSize.Value)
            {
                message = $"{label}: preferred size override ({item.PreferredSize.Value}) must not exceed maximum size override ({item.MaxSize.Value})";
                return false;
            }

            if (!maxUnlimited && item.MinSize.HasValue && item.MinSize.Value > item.MaxSize.Value)
            {
                message = $"{label}: minimum size override ({item.MinSize.Value}) must not exceed maximum size override ({item.MaxSize.Value})";
                return false;
            }

            return true;
        }

        private static string Label(QualityProfileQualityItemResource item)
        {
            if (item.Quality != null)
            {
                var quality = NzbDrone.Core.Qualities.Quality.All.FirstOrDefault(q => q.Id == item.Quality.Id);

                return quality?.Name ?? item.Quality.Name ?? $"quality {item.Quality.Id}";
            }

            return item.Name;
        }

        private static string Describe(double value, SizeLimitSource source, QualityProfileQualityItem group)
        {
            var origin = source switch
            {
                SizeLimitSource.Member => "this profile",
                SizeLimitSource.Group => $"group '{group?.Name}' in this profile",
                _ => "global Quality Definitions"
            };

            return $"{value} ({origin})";
        }
    }
}
