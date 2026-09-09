using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class AudioLanguageProbeCacheFixture : CoreTest
    {
        private class ClockedCache : AudioLanguageProbeCache
        {
            public DateTime Clock { get; set; } = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
            protected override DateTime Now => Clock;
        }

        [Test]
        public void failed_probes_should_expire_after_fifteen_minutes_while_successes_stay_valid()
        {
            var cache = new ClockedCache();
            cache.Set("pack", "aac/2/eng/", 0, null);
            cache.Set("pack", "aac/2/eng/", 1, new AudioLanguageProbeResult { LanguageCode = "fr", Confidence = 0.9 });

            cache.Clock = cache.Clock.AddMinutes(14);
            cache.TryGet("pack", "aac/2/eng/", 0, out _).Should().BeTrue();

            cache.Clock = cache.Clock.AddMinutes(2);
            cache.TryGet("pack", "aac/2/eng/", 0, out _).Should().BeFalse("a failed probe is retried after FailureTtl");
            cache.TryGet("pack", "aac/2/eng/", 1, out var success).Should().BeTrue();
            success.LanguageCode.Should().Be("fr");

            cache.Clock = cache.Clock.AddHours(12);
            cache.TryGet("pack", "aac/2/eng/", 1, out _).Should().BeFalse();
        }

        [Test]
        public void ttls_should_be_fifteen_minutes_and_twelve_hours()
        {
            AudioLanguageProbeCache.FailureTtl.Should().Be(TimeSpan.FromMinutes(15));
            AudioLanguageProbeCache.SuccessTtl.Should().Be(TimeSpan.FromHours(12));
        }

        [Test]
        public void should_miss_on_unknown_key()
        {
            new AudioLanguageProbeCache().TryGet("pack", "aac/2/eng/", 0, out _).Should().BeFalse();
        }

        [Test]
        public void should_return_cached_result_for_same_pack_layout_and_stream()
        {
            var cache = new AudioLanguageProbeCache();
            var result = new AudioLanguageProbeResult { LanguageCode = "fr", Confidence = 0.9 };

            cache.Set("pack", "aac/2/eng/", 0, result);

            cache.TryGet("pack", "aac/2/eng/", 0, out var cached).Should().BeTrue();
            cached.Should().BeSameAs(result);
        }

        [Test]
        public void should_cache_failed_probes_as_null_hits()
        {
            var cache = new AudioLanguageProbeCache();

            cache.Set("pack", "aac/2/eng/", 0, null);

            cache.TryGet("pack", "aac/2/eng/", 0, out var cached).Should().BeTrue();
            cached.Should().BeNull();
        }

        [Test]
        public void should_separate_packs_layouts_and_streams()
        {
            var cache = new AudioLanguageProbeCache();
            cache.Set("pack", "aac/2/eng/", 0, new AudioLanguageProbeResult());

            cache.TryGet("other", "aac/2/eng/", 0, out _).Should().BeFalse();
            cache.TryGet("pack", "ac3/6/eng/", 0, out _).Should().BeFalse();
            cache.TryGet("pack", "aac/2/eng/", 1, out _).Should().BeFalse();
        }

        [Test]
        public void clear_should_drop_everything()
        {
            var cache = new AudioLanguageProbeCache();
            cache.Set("pack", "aac/2/eng/", 0, new AudioLanguageProbeResult());

            cache.Clear();

            cache.TryGet("pack", "aac/2/eng/", 0, out _).Should().BeFalse();
        }
    }
}
