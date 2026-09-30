using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Qualities;
using Sonarr.Api.V3.Profiles.Quality;

namespace NzbDrone.Api.Test.v3.Profiles.Quality;

// krzw(profile-size-limits)
[Parallelizable(ParallelScope.All)]
public class QualityItemSizeLimitsValidatorFixture
{
    private class ItemsValidator : AbstractValidator<QualityProfileResource>
    {
        public ItemsValidator(IQualityDefinitionService qualityDefinitionService)
        {
            RuleFor(c => c.Items).SetValidator(new QualityItemSizeLimitsValidator<QualityProfileResource>(qualityDefinitionService));
        }
    }

    private static ItemsValidator GivenValidator(double? min = 5, double? preferred = 40, double? max = 60)
    {
        var definitions = new Mock<IQualityDefinitionService>();
        definitions.Setup(s => s.Get(It.IsAny<NzbDrone.Core.Qualities.Quality>()))
                   .Returns<NzbDrone.Core.Qualities.Quality>(q => new QualityDefinition(q) { MinSize = min, PreferredSize = preferred, MaxSize = max });

        return new ItemsValidator(definitions.Object);
    }

    private static QualityProfileQualityItemResource Item(NzbDrone.Core.Qualities.Quality quality, double? min = null, double? preferred = null, double? max = null)
    {
        return new QualityProfileQualityItemResource { Quality = quality, Allowed = true, MinSize = min, PreferredSize = preferred, MaxSize = max };
    }

    private static QualityProfileResource Profile(params QualityProfileQualityItemResource[] items)
    {
        return new QualityProfileResource { Name = "Test", Items = items.ToList() };
    }

    [Test]
    public void should_pass_when_nothing_is_overridden()
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p), Item(NzbDrone.Core.Qualities.Quality.SDTV)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [TestCase(-1)]
    [TestCase(1001)]
    public void should_fail_when_override_is_out_of_range(double value)
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, max: value)));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("HDTV-1080p: maximum size override must be between 0 and 1000 MB/min");
    }

    [TestCase(0)]
    [TestCase(1000)]
    public void should_pass_when_override_is_at_range_bounds(double value)
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, min: value == 0 ? 0 : null, max: value)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void should_pass_when_only_max_is_capped_below_the_global_preferred_size()
    {
        // the headline "1080p Light" case: preferred is clamped at runtime, not rejected
        var result = GivenValidator(preferred: 95, max: 125).TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, max: 8)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void should_fail_when_min_override_exceeds_preferred_override_on_the_same_item()
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, min: 10, preferred: 6)));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("HDTV-1080p: minimum size override (10) must not exceed preferred size override (6)");
    }

    [Test]
    public void should_fail_when_preferred_override_exceeds_max_override_on_the_same_item()
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, preferred: 10, max: 8)));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("HDTV-1080p: preferred size override (10) must not exceed maximum size override (8)");
    }

    [Test]
    public void should_pass_when_max_override_is_zero_regardless_of_preferred()
    {
        var result = GivenValidator().TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, preferred: 100, max: 0)));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void should_fail_when_max_override_is_below_the_inherited_global_min()
    {
        var result = GivenValidator(min: 5).TestValidate(Profile(Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, max: 3)));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("HDTV-1080p: minimum size 5 (global Quality Definitions) must not exceed maximum size 3 (this profile)");
    }

    [Test]
    public void should_fail_when_group_max_is_below_the_inherited_global_min_for_a_member()
    {
        var group = new QualityProfileQualityItemResource
        {
            Id = 1001,
            Name = "WEB 1080p",
            Allowed = true,
            MaxSize = 3,
            Items = new List<QualityProfileQualityItemResource> { Item(NzbDrone.Core.Qualities.Quality.WEBDL1080p), Item(NzbDrone.Core.Qualities.Quality.WEBRip1080p) }
        };

        var result = GivenValidator(min: 5).TestValidate(Profile(group));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("WEBDL-1080p: minimum size 5 (global Quality Definitions) must not exceed maximum size 3 (group 'WEB 1080p' in this profile)");
    }

    [Test]
    public void should_let_a_member_override_repair_a_group_conflict()
    {
        var group = new QualityProfileQualityItemResource
        {
            Id = 1001,
            Name = "WEB 1080p",
            Allowed = true,
            MaxSize = 3,
            Items = new List<QualityProfileQualityItemResource> { Item(NzbDrone.Core.Qualities.Quality.WEBDL1080p, min: 1), Item(NzbDrone.Core.Qualities.Quality.WEBRip1080p, min: 2) }
        };

        var result = GivenValidator(min: 5).TestValidate(Profile(group));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void should_label_by_quality_id_when_the_client_sends_no_quality_name()
    {
        var item = Item(NzbDrone.Core.Qualities.Quality.HDTV1080p, max: 5000);
        item.Quality = new NzbDrone.Core.Qualities.Quality { Id = NzbDrone.Core.Qualities.Quality.HDTV1080p.Id };

        var result = GivenValidator().TestValidate(Profile(item));

        result.Errors.Single().ErrorMessage.Should().StartWith("HDTV-1080p:");
    }

    [Test]
    public void should_validate_members_inside_groups()
    {
        var group = new QualityProfileQualityItemResource
        {
            Id = 1001,
            Name = "WEB 1080p",
            Allowed = true,
            Items = new List<QualityProfileQualityItemResource> { Item(NzbDrone.Core.Qualities.Quality.WEBDL1080p, max: 2000), Item(NzbDrone.Core.Qualities.Quality.WEBRip1080p) }
        };

        var result = GivenValidator().TestValidate(Profile(group));

        result.ShouldHaveValidationErrorFor(r => r.Items).WithErrorMessage("WEBDL-1080p: maximum size override must be between 0 and 1000 MB/min");
    }
}
