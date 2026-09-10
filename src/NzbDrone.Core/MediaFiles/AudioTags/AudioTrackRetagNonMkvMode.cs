namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>What to do when the library file is not a Matroska file (MP4, M4V, AVI, ...), which mkvpropedit cannot edit.</summary>
    public enum AudioTrackRetagNonMkvMode
    {
        /// <summary>Leave the file alone and record "skipped-container".</summary>
        Skip = 0,

        /// <summary>Remux the file into a new Matroska file next to it (ffmpeg stream copy, no re-encode) with the corrected tags, then swap it in with the .mkv extension.</summary>
        RemuxToMkv = 1
    }
}
