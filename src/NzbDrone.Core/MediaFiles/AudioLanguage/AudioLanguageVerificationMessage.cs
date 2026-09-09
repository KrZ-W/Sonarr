using System.Globalization;
using System.Linq;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>
    /// Text appended to a language rejection once the audio was actually listened to, so history and
    /// the blocklist say "verified" rather than "tagged". Never changes what is rejected.
    /// </summary>
    public static class AudioLanguageVerificationMessage
    {
        public static string Describe(LocalEpisode localEpisode, Language wantedLanguage)
        {
            if (localEpisode?.AudioLanguageVerification == null || localEpisode.AudioLanguageVerification.Count == 0)
            {
                return null;
            }

            switch (localEpisode.AudioLanguageTrigger)
            {
                case AudioLanguageTrigger.Contradiction:
                case AudioLanguageTrigger.Unknown:
                case AudioLanguageTrigger.ImpendingRejection:
                    break;
                default:
                    return null;
            }

            var detected = string.Join(", ", localEpisode.AudioLanguageVerification.Select(Format));

            if (wantedLanguage != null && wantedLanguage != Language.Unknown)
            {
                return $"Audio verified: no {wantedLanguage.Name} track (detected {detected})";
            }

            return $"Audio verified (detected {detected})";
        }

        private static string Format(AudioLanguageVerification verification)
        {
            if (verification.DetectedLanguage == null)
            {
                return "?";
            }

            return $"{verification.DetectedLanguage} {verification.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}";
        }
    }
}
