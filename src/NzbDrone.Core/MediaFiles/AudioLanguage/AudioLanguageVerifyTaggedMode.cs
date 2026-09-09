namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>When to probe a track that is already tagged with a language the profile expects.</summary>
    public enum AudioLanguageVerifyTaggedMode
    {
        Never = 0,
        ForReleaseGroups = 1,
        Always = 2
    }
}
