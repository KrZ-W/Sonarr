using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Indexers
{
    // krzw(indexer-cooldown): the one parser for the IndexerCooldownPeriods setting (CSV of whole
    // minutes) into the seconds table ProviderStatusServiceBase escalates through. Shared by the
    // status service, the housekeeper that bounds persisted DisabledTill values and the API
    // validator so all three read the same schedule.
    public static class IndexerCooldownPeriods
    {
        // Largest minute value whose seconds equivalent still fits in the int table.
        private const int MaxMinutes = int.MaxValue / 60;

        public static bool IsValid(string csv)
        {
            return TryParseMinutes(csv, out _);
        }

        // Blank, unparsable or negative input falls back to the upstream default table.
        public static int[] ToSecondsTable(string csv)
        {
            if (!TryParseMinutes(csv, out var minutes) || minutes.Count == 0)
            {
                return EscalationBackOff.Periods;
            }

            // Level 0 is "healthy" and must carry no cooldown.
            if (minutes[0] != 0)
            {
                minutes.Insert(0, 0);
            }

            return minutes.Select(m => m * 60).ToArray();
        }

        private static bool TryParseMinutes(string csv, out List<int> minutes)
        {
            minutes = new List<int>();

            if (csv.IsNullOrWhiteSpace())
            {
                return true;
            }

            foreach (var part in csv.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                // NumberStyles.None rejects signs, so negatives are invalid rather than silently
                // producing a cooldown in the past.
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > MaxMinutes)
                {
                    minutes = null;
                    return false;
                }

                minutes.Add(value);
            }

            return true;
        }
    }
}
