using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public abstract class FixFutureProviderStatusTimes<TModel>
        where TModel : ProviderStatusBase, new()
    {
        private readonly IProviderStatusRepository<TModel> _repo;

        protected FixFutureProviderStatusTimes(IProviderStatusRepository<TModel> repo)
        {
            _repo = repo;
        }

        // krzw(indexer-cooldown): the schedule the bound is computed from. Indexers override this with
        // the configured IndexerCooldownPeriods so a longer custom cooldown is not clipped back to the
        // default table by the daily housekeeping run.
        protected virtual int[] GetEscalationPeriods()
        {
            return EscalationBackOff.Periods;
        }

        public void Clean()
        {
            var now = DateTime.UtcNow;
            var statuses = _repo.All().ToList();
            var toUpdate = new List<TModel>();
            var periods = GetEscalationPeriods();  // krzw(indexer-cooldown)

            foreach (var status in statuses)
            {
                var updated = false;

                // krzw(indexer-cooldown): a persisted EscalationLevel can exceed the last index when the
                // schedule was shortened since the failure was recorded - clamp it, matching
                // CalculateBackOffPeriod, instead of throwing and aborting the whole housekeeper.
                var escalationLevel = Math.Min(status.EscalationLevel, periods.Length - 1);
                var escalationDelay = periods[escalationLevel];
                var disabledTill = now.AddMinutes(escalationDelay);

                if (status.DisabledTill > disabledTill)
                {
                    status.DisabledTill = disabledTill;
                    updated = true;
                }

                if (status.InitialFailure > now)
                {
                    status.InitialFailure = now;
                    updated = true;
                }

                if (status.MostRecentFailure > now)
                {
                    status.MostRecentFailure = now;
                    updated = true;
                }

                if (updated)
                {
                    toUpdate.Add(status);
                }
            }

            _repo.UpdateMany(toUpdate);
        }
    }
}
