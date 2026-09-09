using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv.ImdbTitles;

namespace NzbDrone.Core.Test.TvTests.ImdbTitleTests
{
    [TestFixture]
    public class ImdbAkasDatabaseFixture : CoreTest<ImdbAkasDatabase>
    {
        private string _path;
        private ImdbAkasDatabase _database;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(f => f.AppDataFolder).Returns(TempFolder);

            _path = Path.Combine(TempFolder, "imdb-akas.db");
            _database = Subject;
            _database.Path.Should().Be(_path);
        }

        private static ImdbAkasRow Row(string tconst, string title, string region = null, string language = null)
        {
            return new ImdbAkasRow { Tconst = tconst, Title = title, Region = region, Language = language };
        }

        private static IEnumerable<ImdbAkasRow> ThrowAfter(IEnumerable<ImdbAkasRow> rows, int count)
        {
            var i = 0;

            foreach (var row in rows)
            {
                if (i++ >= count)
                {
                    throw new IOException("dump truncated");
                }

                yield return row;
            }
        }

        [Test]
        public void should_report_missing_database()
        {
            _database.Exists.Should().BeFalse();
            _database.GetInfo().Should().BeNull();
            _database.GetTitles("tt0211915").Should().BeEmpty();
        }

        [Test]
        public void should_build_and_query_by_tconst_in_dataset_order()
        {
            var rows = new[]
            {
                Row("tt0211915", "Le Fabuleux Destin d'Amélie Poulain", "FR"),
                Row("tt0068646", "Le Parrain", "CA"),
                Row("tt0211915", "Amélie de Montmartre", "CA", "fr")
            };

            var count = _database.Build(rows, new ImdbAkasDatabaseInfo { ETag = "\"abc\"", LastModified = "Mon, 01 Sep 2025 00:00:00 GMT", FilterSignature = "sig" });

            count.Should().Be(3);
            _database.Exists.Should().BeTrue();
            File.Exists(_path + ".tmp").Should().BeFalse();

            var titles = _database.GetTitles("tt0211915");
            titles.Select(t => t.Title).Should().Equal("Le Fabuleux Destin d'Amélie Poulain", "Amélie de Montmartre");
            titles[0].Region.Should().Be("FR");
            titles[0].Language.Should().BeNull();
            titles[1].Language.Should().Be("fr");

            var info = _database.GetInfo();
            info.ETag.Should().Be("\"abc\"");
            info.LastModified.Should().Be("Mon, 01 Sep 2025 00:00:00 GMT");
            info.FilterSignature.Should().Be("sig");
            info.RowCount.Should().Be(3);
            info.BuiltAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void should_replace_previous_database_atomically()
        {
            _database.Build(new[] { Row("tt0211915", "Old title") }, new ImdbAkasDatabaseInfo { ETag = "1" });
            _database.Build(new[] { Row("tt0211915", "New title") }, new ImdbAkasDatabaseInfo { ETag = "2" });

            _database.GetTitles("tt0211915").Select(t => t.Title).Should().Equal("New title");
            _database.GetInfo().ETag.Should().Be("2");
        }

        [Test]
        public void should_keep_previous_database_when_build_fails()
        {
            _database.Build(new[] { Row("tt0211915", "Old title") }, new ImdbAkasDatabaseInfo { ETag = "1" });

            var failing = ThrowAfter(new[] { Row("tt0211915", "New title"), Row("tt0068646", "Le Parrain") }, 1);

            Assert.Throws<IOException>(() => _database.Build(failing, new ImdbAkasDatabaseInfo { ETag = "2" }));

            _database.GetTitles("tt0211915").Select(t => t.Title).Should().Equal("Old title");
            _database.GetInfo().ETag.Should().Be("1");
            File.Exists(_path + ".tmp").Should().BeFalse();
        }

        [Test]
        public void should_not_create_database_when_first_build_fails()
        {
            Assert.Throws<IOException>(() => _database.Build(ThrowAfter(new[] { Row("tt0211915", "New title") }, 0), new ImdbAkasDatabaseInfo()));

            _database.Exists.Should().BeFalse();
            File.Exists(_path + ".tmp").Should().BeFalse();
        }
    }
}
