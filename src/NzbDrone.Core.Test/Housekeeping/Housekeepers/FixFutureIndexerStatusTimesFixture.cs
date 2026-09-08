using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class FixFutureIndexerStatusTimesFixture : CoreTest<FixFutureIndexerStatusTimes>
    {
        [Test]
        public void should_set_disabled_till_when_its_too_far_in_the_future()
        {
            var disabledTillTime = EscalationBackOff.Periods[1];
            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddDays(5))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.EscalationLevel = 1)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.All(
                              s => s.DisabledTill.Value < DateTime.UtcNow.AddMinutes(disabledTillTime)))));
        }

        [Test]
        public void should_set_initial_failure_when_its_in_the_future()
        {
            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.EscalationLevel = 1)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.All(
                              s => s.InitialFailure.Value <= DateTime.UtcNow))));
        }

        [Test]
        public void should_set_most_recent_failure_when_its_in_the_future()
        {
            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(5))
                                                        .With(t => t.EscalationLevel = 1)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.All(
                              s => s.MostRecentFailure.Value <= DateTime.UtcNow))));
        }

        [Test]
        public void should_clamp_escalation_level_beyond_the_schedule()
        {
            // krzw(indexer-cooldown): a persisted level past the end of the table must not throw (which
            // aborted the whole housekeeper) but be bounded by the last period.
            var maxDelay = EscalationBackOff.Periods[EscalationBackOff.Periods.Length - 1];
            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddDays(100))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.EscalationLevel = EscalationBackOff.Periods.Length + 5)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.Count == 5 && i.All(
                              s => s.DisabledTill.Value <= DateTime.UtcNow.AddMinutes(maxDelay)))));
        }

        [Test]
        public void should_bound_disabled_till_with_the_configured_cooldown_schedule()
        {
            // krzw(indexer-cooldown): level 1 is 60 minutes here, far above the default table's level 1,
            // so a DisabledTill the status service legitimately set must not be clipped by housekeeping.
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.IndexerCooldownPeriods)
                  .Returns("0,60");

            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddMinutes(100))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.EscalationLevel = 1)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.Count == 0)));
        }

        [Test]
        public void should_not_change_statuses_when_times_are_in_the_past()
        {
            var indexerStatuses = Builder<IndexerStatus>.CreateListOfSize(5)
                                                        .All()
                                                        .With(t => t.DisabledTill = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.InitialFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.MostRecentFailure = DateTime.UtcNow.AddDays(-5))
                                                        .With(t => t.EscalationLevel = 0)
                                                        .BuildListOfNew();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Setup(s => s.All())
                  .Returns(indexerStatuses);

            Subject.Clean();

            Mocker.GetMock<IIndexerStatusRepository>()
                  .Verify(v => v.UpdateMany(
                          It.Is<List<IndexerStatus>>(i => i.Count == 0)));
        }
    }
}
