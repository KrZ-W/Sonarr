namespace NzbDrone.Core.MediaFiles.EpisodeImport;

public enum ImportRejectionReason
{
    Unknown,
    FileLocked,
    UnknownSeries,
    DangerousFile,
    ExecutableFile,
    ArchiveFile,
    SeriesFolder,
    InvalidFilePath,
    UnsupportedExtension,
    PartialSeason,
    SeasonExtra,
    InvalidSeasonOrEpisode,
    UnableToParse,
    Error,
    DecisionError,
    NoEpisodes,
    MissingAbsoluteEpisodeNumber,
    EpisodeAlreadyImported,
    TitleMissing,
    TitleTba,
    MinimumFreeSpace,
    FullSeason,
    NoAudio,
    EpisodeUnexpected,
    EpisodeNotFoundInRelease,
    Sample,
    SampleIndeterminate,
    Unpacking,
    ExistingFileHasMoreEpisodes,
    SplitEpisode,
    UnverifiedSceneMapping,
    NotQualityUpgrade,
    NotRevisionUpgrade,
    NotCustomFormatUpgrade,
    CustomFormatMinimumScore,  // krzw(import-enforcement)
    SourceIsSymlink,  // krzw(symlink-import-guard)
    SourceIsSymlinkToLibraryFile,  // krzw(symlink-import-guard): the link resolves to a tracked library file of another episode
    SourceIsSymlinkToCurrentFile  // krzw(symlink-import-guard): the link resolves to the current file of the very episode(s) it maps to
}
