using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Test.IndexerTests
{
    // krzw(indexer-cooldown)
    [TestFixture]
    public class IndexerCooldownPeriodsFixture
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(",")]
        public void should_use_default_table_when_blank(string csv)
        {
            IndexerCooldownPeriods.ToSecondsTable(csv).Should().BeSameAs(EscalationBackOff.Periods);
            IndexerCooldownPeriods.IsValid(csv).Should().BeTrue();
        }

        [Test]
        public void should_convert_minutes_to_seconds()
        {
            IndexerCooldownPeriods.ToSecondsTable("0, 2,10,30,120").Should().Equal(0, 120, 600, 1800, 7200);
        }

        [Test]
        public void should_prepend_healthy_level_when_first_value_is_not_zero()
        {
            IndexerCooldownPeriods.ToSecondsTable("5,15").Should().Equal(0, 300, 900);
        }

        [TestCase("0,-5,10")]
        [TestCase("0,abc")]
        [TestCase("0,1.5")]
        [TestCase("0,99999999999")]
        [TestCase("0,40000000")]
        public void should_reject_invalid_input_and_fall_back_to_default(string csv)
        {
            IndexerCooldownPeriods.IsValid(csv).Should().BeFalse();
            IndexerCooldownPeriods.ToSecondsTable(csv).Should().BeSameAs(EscalationBackOff.Periods);
        }
    }
}
