using System;
using System.Diagnostics;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    /// <summary>
    /// Language detection through whisper-asr-webservice (the container Bazarr users run):
    /// POST {endpoint}/detect-language with a short WAV clip of one audio stream.
    /// </summary>
    public class WhisperAsrAudioLanguageProbe : IAudioLanguageProbe
    {
        private readonly IAudioClipExtractor _clipExtractor;
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public WhisperAsrAudioLanguageProbe(IAudioClipExtractor clipExtractor,
                                            IHttpClient httpClient,
                                            IConfigService configService,
                                            Logger logger)
        {
            _clipExtractor = clipExtractor;
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        public AudioLanguageProbeResult Probe(string path, int audioStreamIndex, TimeSpan? runtime)
        {
            try
            {
                var endpoint = _configService.AudioLanguageVerificationEndpoint;

                if (endpoint.IsNullOrWhiteSpace())
                {
                    _logger.Warn("Audio language verification has no endpoint configured, skipping the probe");
                    return null;
                }

                // The setting is a per-track total: the clip extraction gets at most a third of it, the
                // detector call whatever is left once the clip exists.
                var budget = TimeSpan.FromSeconds(Math.Max(1, _configService.AudioLanguageVerificationTimeout));
                var length = TimeSpan.FromSeconds(Math.Max(1, _configService.AudioLanguageVerificationClipLength));
                var offset = ClipOffset(TimeSpan.FromSeconds(Math.Max(0, _configService.AudioLanguageVerificationClipOffset)), length, runtime);

                var stopwatch = Stopwatch.StartNew();
                var clip = _clipExtractor.Extract(path, audioStreamIndex, offset, length, ExtractionTimeout(budget));

                if (clip == null || clip.Length == 0)
                {
                    _logger.Warn("Audio language verification skipped for stream {0} of '{1}': no clip could be extracted", audioStreamIndex, path);
                    return null;
                }

                var timeout = DetectorTimeout(budget, stopwatch.Elapsed);

                if (timeout <= TimeSpan.Zero)
                {
                    _logger.Warn("Audio language verification skipped for stream {0} of '{1}': the clip extraction used the whole {2}s budget", audioStreamIndex, path, (int)budget.TotalSeconds);
                    return null;
                }

                var request = new HttpRequestBuilder(endpoint)
                    .Resource("detect-language")
                    .Post()
                    .Accept(HttpAccept.Json)
                    .AddFormUpload("audio_file", "clip.wav", clip, "audio/wav")
                    .Build();

                request.RequestTimeout = timeout;
                request.LogHttpError = false;
                request.SuppressHttpError = true;

                var response = _httpClient.Post(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Warn("Audio language verification failed for stream {0} of '{1}': {2} returned {3}", audioStreamIndex, path, endpoint, response.StatusCode);
                    return null;
                }

                var body = Json.Deserialize<WhisperDetectLanguageResponse>(response.Content);

                if (body?.LanguageCode.IsNullOrWhiteSpace() ?? true)
                {
                    _logger.Warn("Audio language verification failed for stream {0} of '{1}': no language in the detector reply", audioStreamIndex, path);
                    return null;
                }

                var code = body.LanguageCode.Trim().ToLowerInvariant();
                var language = IsoLanguages.Find(code)?.Language;

                _logger.Debug("Audio stream {0} of '{1}' detected as {2} ({3:0.00})", audioStreamIndex, path, code, body.Confidence);

                return new AudioLanguageProbeResult
                {
                    LanguageCode = code,
                    Language = language == Languages.Language.Unknown ? null : language,
                    Confidence = body.Confidence
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Audio language verification failed for stream {0} of '{1}', importing on existing evidence", audioStreamIndex, path);
                return null;
            }
        }

        /// <summary>At most a third of the per-track budget goes to ffmpeg.</summary>
        public static TimeSpan ExtractionTimeout(TimeSpan budget)
        {
            return TimeSpan.FromTicks(budget.Ticks / 3);
        }

        /// <summary>Whatever is left of the per-track budget after the extraction; zero or less means "out of time".</summary>
        public static TimeSpan DetectorTimeout(TimeSpan budget, TimeSpan extractionElapsed)
        {
            return budget - extractionElapsed;
        }

        /// <summary>Configured offset, or the middle of the file when it is too short for offset + length.</summary>
        public static TimeSpan ClipOffset(TimeSpan configuredOffset, TimeSpan length, TimeSpan? runtime)
        {
            if (runtime == null || runtime.Value <= TimeSpan.Zero)
            {
                return configuredOffset;
            }

            if (runtime.Value >= configuredOffset + length)
            {
                return configuredOffset;
            }

            var middle = (runtime.Value - length) / 2;

            return middle < TimeSpan.Zero ? TimeSpan.Zero : middle;
        }

        public class WhisperDetectLanguageResponse
        {
            [Newtonsoft.Json.JsonProperty("detected_language")]
            public string DetectedLanguage { get; set; }

            [Newtonsoft.Json.JsonProperty("language_code")]
            public string LanguageCode { get; set; }

            [Newtonsoft.Json.JsonProperty("confidence")]
            public double Confidence { get; set; }
        }
    }
}
