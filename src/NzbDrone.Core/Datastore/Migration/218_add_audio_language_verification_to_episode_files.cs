using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(audio-language-verification): per-track probe outcome stored as JSON, written at import only
    [Migration(218)]
    public class add_audio_language_verification_to_episode_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("EpisodeFiles").AddColumn("AudioLanguageVerification").AsString().Nullable();
        }
    }
}
