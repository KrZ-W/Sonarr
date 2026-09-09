using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.AudioLanguage
{
    [TestFixture]
    public class AudioLanguageProbeCacheFixture : CoreTest
    {
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
