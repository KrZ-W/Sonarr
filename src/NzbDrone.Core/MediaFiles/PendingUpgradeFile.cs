namespace NzbDrone.Core.MediaFiles
{
    // Tracks an existing episode file that has been parked aside (renamed) during an upgrade import so
    // that the destination slot is free for the replacement, while the original stays fully recoverable
    // until the import is committed. See UpgradeMediaFileService for the park/finalize/rollback flow.
    public class PendingUpgradeFile
    {
        public EpisodeFile EpisodeFile { get; set; }

        // The path the file occupied in the library before it was parked.
        public string OriginalPath { get; set; }

        // Where the file was parked. Null when the original was already missing from disk (nothing parked).
        public string BackupPath { get; set; }

        // Recycle-bin subfolder for the original, computed before parking.
        public string Subfolder { get; set; }

        // The OldFiles entry exposed to the import script and notifications. Created at park time so
        // consumers that run during the transfer (ScriptImportDecider) see it; its RecycleBinPath is
        // filled in by FinalizeUpgrade.
        public DeletedEpisodeFile Deleted { get; set; }
    }
}
