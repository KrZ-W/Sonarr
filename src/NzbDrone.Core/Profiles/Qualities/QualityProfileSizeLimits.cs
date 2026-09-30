using System.Linq;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    // krzw(profile-size-limits)
    // Where an effective size limit came from.
    public enum SizeLimitSource
    {
        None,
        Global,
        Group,
        Member
    }

    // krzw(profile-size-limits)
    // Effective size limits (MB/min) for a quality inside a given profile.
    // Resolution order per field: profile member item > profile group item > global QualityDefinition.
    // Null MinSize / PreferredSize = none. MaxSize null or 0 = unlimited.
    // PreferredSize is clamped into [MinSize, MaxSize] so the release ordering can never
    // prefer a size the size specification would reject (e.g. a profile that only caps MaxSize).
    public class EffectiveSizeLimits
    {
        public double? MinSize { get; set; }
        public double? MaxSize { get; set; }
        public double? PreferredSize { get; set; }

        public SizeLimitSource MinSizeSource { get; set; }
        public SizeLimitSource MaxSizeSource { get; set; }
        public SizeLimitSource PreferredSizeSource { get; set; }

        public bool PreferredSizeClamped { get; set; }

        public bool IsMaxUnlimited => !MaxSize.HasValue || MaxSize.Value == 0;
    }

    public static class QualityProfileSizeLimits
    {
        public static EffectiveSizeLimits Resolve(QualityProfile profile, Quality quality, QualityDefinition definition)
        {
            var (group, member) = FindItems(profile, quality);

            var limits = new EffectiveSizeLimits();

            (limits.MinSize, limits.MinSizeSource) = Pick(member?.MinSize, group?.MinSize, definition?.MinSize);
            (limits.MaxSize, limits.MaxSizeSource) = Pick(member?.MaxSize, group?.MaxSize, definition?.MaxSize);
            (limits.PreferredSize, limits.PreferredSizeSource) = Pick(member?.PreferredSize, group?.PreferredSize, definition?.PreferredSize);

            if (limits.PreferredSize.HasValue)
            {
                if (!limits.IsMaxUnlimited && limits.PreferredSize.Value > limits.MaxSize.Value)
                {
                    limits.PreferredSize = limits.MaxSize;
                    limits.PreferredSizeClamped = true;
                }

                if (limits.MinSize.HasValue && limits.PreferredSize.Value < limits.MinSize.Value)
                {
                    limits.PreferredSize = limits.MinSize;
                    limits.PreferredSizeClamped = true;
                }
            }

            return limits;
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

        private static (double? Value, SizeLimitSource Source) Pick(double? member, double? group, double? global)
        {
            if (member.HasValue)
            {
                return (member, SizeLimitSource.Member);
            }

            if (group.HasValue)
            {
                return (group, SizeLimitSource.Group);
            }

            if (global.HasValue)
            {
                return (global, SizeLimitSource.Global);
            }

            return (null, SizeLimitSource.None);
        }
    }
}
