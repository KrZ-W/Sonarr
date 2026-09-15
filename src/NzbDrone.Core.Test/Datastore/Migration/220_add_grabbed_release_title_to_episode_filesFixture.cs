using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class add_grabbed_release_title_to_episode_filesFixture : MigrationTest<add_grabbed_release_title_to_episode_files>
    {
        [Test]
        public void should_add_nullable_column_and_keep_existing_rows()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("EpisodeFiles").Row(new
                {
                    SeriesId = 1,
                    SeasonNumber = 1,
                    Quality = new { Quality = 6 }.ToJson(),
                    Size = 997478103,
                    DateAdded = DateTime.Now,
                    Languages = "[2]",
                    IndexerFlags = 0,
                    ReleaseType = 0,
                    RelativePath = "Season 01/Series - S01E01.mkv"
                });
            });

            var rows = db.Query<EpisodeFile220>("SELECT \"Id\", \"GrabbedReleaseTitle\" FROM \"EpisodeFiles\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().GrabbedReleaseTitle.Should().BeNull();
        }

        private class EpisodeFile220
        {
            public int Id { get; set; }
            public string GrabbedReleaseTitle { get; set; }
        }
    }
}
