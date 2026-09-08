using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    public class EpisodeFileMoveResult
    {
        public EpisodeFileMoveResult()
        {
            OldFiles = new List<DeletedEpisodeFile>();
            PendingUpgrades = new List<PendingUpgradeFile>();  // krzw(atomic-upgrade)
        }

        public EpisodeFile EpisodeFile { get; set; }
        public List<DeletedEpisodeFile> OldFiles { get; set; }

        // krzw(atomic-upgrade): park/finalize/rollback bookkeeping
        // Existing files parked aside for this import, pending FinalizeUpgrade (commit) or RollbackUpgrade.
        public List<PendingUpgradeFile> PendingUpgrades { get; set; }

        // Absolute path the replacement file was moved/copied to, used to clean up on rollback.
        public string NewFilePath { get; set; }

        // Where the replacement file came from (download location). On rollback a moved file is
        // returned here so the download stays importable; a copied/hardlinked file is just deleted.
        public string SourcePath { get; set; }
    }
}
