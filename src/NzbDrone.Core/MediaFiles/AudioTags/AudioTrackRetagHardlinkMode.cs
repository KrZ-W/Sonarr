namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>What to do when the library file shares its bytes with another path (a seeding hardlink).</summary>
    public enum AudioTrackRetagHardlinkMode
    {
        /// <summary>Leave the file alone and record "skipped-hardlinked".</summary>
        Skip = 0,

        /// <summary>Copy the file next to itself, retag the copy, rename it over the library path; the seed keeps its bytes.</summary>
        CopyThenRetag = 1,

        /// <summary>Edit the shared bytes; every hardlinked path (the seed included) sees the new tags and fails hash checks.</summary>
        RetagInPlace = 2
    }
}
