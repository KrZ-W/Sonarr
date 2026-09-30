using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Profiles
{
    // krzw(profile-size-limits)
    [TestFixture]
    public class QualityProfileSizeLimitsFixture : CoreTest
    {
        private QualityDefinition _definition;
        private QualityProfileQualityItem _member;
        private QualityProfileQualityItem _group;
        private QualityProfile _profile;

        [SetUp]
        public void Setup()
        {
            _definition = new QualityDefinition(Quality.HDTV1080p) { MinSize = 5, MaxSize = 60, PreferredSize = 40 };
            _member = new QualityProfileQualityItem { Quality = Quality.HDTV1080p, Allowed = true };
            _group = new QualityProfileQualityItem
            {
                Id = 1001,
                Name = "HD 1080p",
                Allowed = true,
                Items = new List<QualityProfileQualityItem>
                {
                    _member,
                    new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, Allowed = true }
                }
            };

            _profile = new QualityProfile
            {
                Items = new List<QualityProfileQualityItem>
                {
                    new QualityProfileQualityItem { Quality = Quality.SDTV, Allowed = true, MaxSize = 3 },
                    _group
                }
            };
        }

        [Test]
        public void should_fall_back_to_definition_when_nothing_is_overridden()
        {
            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MinSize.Should().Be(5);
            limits.MaxSize.Should().Be(60);
            limits.PreferredSize.Should().Be(40);
        }

        [Test]
        public void should_fall_back_to_definition_when_profile_is_null()
        {
            var limits = QualityProfileSizeLimits.Resolve(null, Quality.HDTV1080p, _definition);

            limits.MaxSize.Should().Be(60);
        }

        [Test]
        public void should_fall_back_to_definition_when_quality_is_not_in_profile()
        {
            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.Bluray2160p, _definition);

            limits.MaxSize.Should().Be(60);
        }

        [Test]
        public void should_use_group_override_for_member()
        {
            _group.MaxSize = 8;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MaxSize.Should().Be(8);
            limits.MinSize.Should().Be(5);
        }

        [Test]
        public void should_use_member_override_over_group_and_definition()
        {
            _group.MaxSize = 8;
            _member.MaxSize = 12;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MaxSize.Should().Be(12);
        }

        [Test]
        public void should_resolve_each_field_independently()
        {
            _group.PreferredSize = 6;
            _member.MaxSize = 8;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MinSize.Should().Be(5);
            limits.PreferredSize.Should().Be(6);
            limits.MaxSize.Should().Be(8);
        }

        [Test]
        public void should_treat_zero_max_override_as_unlimited_even_if_definition_has_a_max()
        {
            _member.MaxSize = 0;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MaxSize.Should().Be(0);
            limits.IsMaxUnlimited.Should().BeTrue();
        }

        [Test]
        public void should_use_top_level_item_override()
        {
            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.SDTV, new QualityDefinition(Quality.SDTV) { MaxSize = 100 });

            limits.MaxSize.Should().Be(3);
        }

        [Test]
        public void should_use_overrides_when_the_definition_has_no_limits_at_all()
        {
            _member.MinSize = 3;
            _member.PreferredSize = 6;
            _member.MaxSize = 8;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, new QualityDefinition(Quality.HDTV1080p));

            limits.MinSize.Should().Be(3);
            limits.PreferredSize.Should().Be(6);
            limits.MaxSize.Should().Be(8);
            limits.MinSizeSource.Should().Be(SizeLimitSource.Member);
        }

        [Test]
        public void should_report_the_source_of_each_field()
        {
            _group.MaxSize = 8;
            _member.MinSize = 1;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.MinSizeSource.Should().Be(SizeLimitSource.Member);
            limits.MaxSizeSource.Should().Be(SizeLimitSource.Group);
            limits.PreferredSizeSource.Should().Be(SizeLimitSource.Global);
        }

        [Test]
        public void should_clamp_inherited_preferred_size_down_to_an_overridden_max()
        {
            _member.MaxSize = 8;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.PreferredSize.Should().Be(8);
            limits.PreferredSizeClamped.Should().BeTrue();
        }

        [Test]
        public void should_clamp_preferred_size_up_to_an_overridden_min()
        {
            _member.MinSize = 50;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.PreferredSize.Should().Be(50);
            limits.PreferredSizeClamped.Should().BeTrue();
        }

        [Test]
        public void should_not_clamp_preferred_size_when_max_is_unlimited()
        {
            _member.MaxSize = 0;
            _member.PreferredSize = 500;

            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.PreferredSize.Should().Be(500);
            limits.PreferredSizeClamped.Should().BeFalse();
        }

        [Test]
        public void should_not_clamp_when_nothing_is_overridden()
        {
            var limits = QualityProfileSizeLimits.Resolve(_profile, Quality.HDTV1080p, _definition);

            limits.PreferredSize.Should().Be(40);
            limits.PreferredSizeClamped.Should().BeFalse();
        }
    }
}
