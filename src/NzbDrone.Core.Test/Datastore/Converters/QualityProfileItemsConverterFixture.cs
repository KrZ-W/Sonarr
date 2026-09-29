using System.Collections.Generic;
using System.Data.SQLite;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Converters
{
    // krzw(profile-size-limits)
    // The profile items are an embedded JSON document in the QualityProfiles row. The size
    // overrides must round-trip, and rows written before the feature must deserialize unchanged.
    [TestFixture]
    public class QualityProfileItemsConverterFixture : CoreTest
    {
        private const string LegacyJson = @"[
  {
    ""quality"": 1,
    ""items"": [],
    ""allowed"": true
  },
  {
    ""id"": 1000,
    ""name"": ""WEB 720p"",
    ""items"": [
      { ""quality"": 5, ""items"": [], ""allowed"": true },
      { ""quality"": 14, ""items"": [], ""allowed"": true }
    ],
    ""allowed"": true
  }
]";

        private EmbeddedDocumentConverter<List<QualityProfileQualityItem>> Subject => new (new QualityIntConverter());

        [Test]
        public void should_deserialize_legacy_items_without_overrides()
        {
            var items = Subject.Parse(LegacyJson);

            items.Should().HaveCount(2);
            items[0].Quality.Should().Be(Quality.SDTV);
            items[0].MinSize.Should().BeNull();
            items[0].MaxSize.Should().BeNull();
            items[0].PreferredSize.Should().BeNull();
            items[1].Items.Should().HaveCount(2);
            items[1].MaxSize.Should().BeNull();
            items[1].Items[0].MaxSize.Should().BeNull();
        }

        [Test]
        public void should_not_write_override_properties_when_none_are_set()
        {
            var items = Subject.Parse(LegacyJson);
            var param = new SQLiteParameter();

            Subject.SetValue(param, items);

            var json = (string)param.Value;
            json.Should().NotContain("minSize");
            json.Should().NotContain("maxSize");
            json.Should().NotContain("preferredSize");
        }

        [Test]
        public void should_round_trip_overrides_on_members_and_groups()
        {
            var items = new List<QualityProfileQualityItem>
            {
                new QualityProfileQualityItem { Quality = Quality.HDTV1080p, Allowed = true, MinSize = 2.5, MaxSize = 0, PreferredSize = 6 },
                new QualityProfileQualityItem
                {
                    Id = 1001,
                    Name = "HD",
                    Allowed = true,
                    MaxSize = 8,
                    Items = new List<QualityProfileQualityItem>
                    {
                        new QualityProfileQualityItem { Quality = Quality.WEBDL1080p, Allowed = true, PreferredSize = 7 },
                        new QualityProfileQualityItem { Quality = Quality.Bluray1080p, Allowed = true }
                    }
                }
            };

            var param = new SQLiteParameter();
            Subject.SetValue(param, items);
            var result = Subject.Parse(param.Value);

            result[0].MinSize.Should().Be(2.5);
            result[0].MaxSize.Should().Be(0);
            result[0].PreferredSize.Should().Be(6);
            result[1].MaxSize.Should().Be(8);
            result[1].MinSize.Should().BeNull();
            result[1].Items[0].PreferredSize.Should().Be(7);
            result[1].Items[0].MaxSize.Should().BeNull();
            result[1].Items[1].MaxSize.Should().BeNull();
        }
    }
}
