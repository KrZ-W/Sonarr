namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>Why the audio language probe ran for a file, in policy order.</summary>
    public enum AudioLanguageTrigger
    {
        None = 0,

        /// <summary>The release/folder/file/grab claimed a language no audio track is tagged with.</summary>
        Contradiction = 1,

        /// <summary>At least one audio track is tagged und or carries no language tag.</summary>
        Unknown = 2,

        /// <summary>With the current tags the import-time language check or a language custom format would reject the file.</summary>
        ImpendingRejection = 3,

        /// <summary>A track is tagged with an expected language and the "verify tagged tracks" setting asked for a check.</summary>
        PositiveVerification = 4
    }
}
