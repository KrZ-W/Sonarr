using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(grabbed-release-title): the grab's release title, kept next to SceneName so custom formats can be scored against it
    [Migration(220)]
    public class add_grabbed_release_title_to_episode_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("EpisodeFiles").AddColumn("GrabbedReleaseTitle").AsString().Nullable();
        }
    }
}
