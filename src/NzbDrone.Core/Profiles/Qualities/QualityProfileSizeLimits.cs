using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    // krzw(profile-size-limits)
    // Effective size limits (MB/min) for a quality inside a given profile.
    // Resolution order: profile member item > profile group item > global QualityDefinition.
    // Null MinSize / PreferredSize = none. MaxSize null or 0 = unlimited.
    public class EffectiveSizeLimits
    {
        public double? MinSize { get; set; }
        public double? MaxSize { get; set; }
        public double? PreferredSize { get; set; }

        public bool IsMaxUnlimited => !MaxSize.HasValue || MaxSize.Value == 0;
    }

    public static class QualityProfileSizeLimits
    {
        public static EffectiveSizeLimits Resolve(QualityProfile profile, Quality quality, QualityDefinition definition)
        {
            var (group, member) = FindItems(profile, quality);

            return new EffectiveSizeLimits
            {
                MinSize = member?.MinSize ?? group?.MinSize ?? definition?.MinSize,
                MaxSize = member?.MaxSize ?? group?.MaxSize ?? definition?.MaxSize,
                PreferredSize = member?.PreferredSize ?? group?.PreferredSize ?? definition?.PreferredSize
            };
        }

        public static bool HasOverride(QualityProfileQualityItem item)
        {
            return item != null && (item.MinSize.HasValue || item.MaxSize.HasValue || item.PreferredSize.HasValue);
        }

        public static (QualityProfileQualityItem Group, QualityProfileQualityItem Member) FindItems(QualityProfile profile, Quality quality)
        {
            if (profile?.Items == null || quality == null)
            {
                return (null, null);
            }

            foreach (var item in profile.Items)
            {
                if (item.Quality != null)
                {
                    if (item.Quality == quality)
                    {
                        return (null, item);
                    }

                    continue;
                }

                var member = item.Items?.FirstOrDefault(i => i.Quality == quality);

                if (member != null)
                {
                    return (item, member);
                }
            }

            return (null, null);
        }
    }
}
