using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_audio_track_retag_to_episode_filesFixture : MigrationTest<add_audio_track_retag_to_episode_files>
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

            var rows = db.Query<EpisodeFile219>("SELECT \"Id\", \"AudioTrackRetag\" FROM \"EpisodeFiles\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().AudioTrackRetag.Should().BeNull();
        }

        private class EpisodeFile219
        {
            public int Id { get; set; }
            public string AudioTrackRetag { get; set; }
        }
    }
}
