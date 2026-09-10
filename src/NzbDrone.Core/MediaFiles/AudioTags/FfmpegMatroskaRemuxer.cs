using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// ffmpeg wrapper for the "Non-MKV files: Remux to MKV" path: one stream-copy pass
    /// (-c copy, never a re-encode) from the source container into a new Matroska file, with
    /// the corrected audio language written as -metadata:s:a:N in the same call so no
    /// mkvpropedit pass is needed afterwards. Every video, audio, subtitle and attachment
    /// stream is mapped explicitly; only the streams Matroska cannot carry (MP4 timed text,
    /// closed-caption and timecode/data streams) are left out, and they are reported.
    /// The same process runner and binary resolution as the verification clip extractor.
    /// </summary>
    public class FfmpegMatroskaRemuxer : IAudioTrackRemuxer
    {
        /// <summary>Lines of ffmpeg stderr kept for the error record.</summary>
        public const int StderrTailLines = 8;

        /// <summary>Codecs (ffprobe codec_name) that have no Matroska mapping in ffmpeg's muxer; dropped with a log line instead of failing the whole file.</summary>
        public static readonly HashSet<string> UnsupportedInMatroska = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mov_text",      // MP4 timed text (tx3g)
            "eia_608",       // closed captions carried as a stream
            "dvb_teletext",
            "xsub",          // DivX AVI subtitles
            "microdvd",
            "tmcd"           // QuickTime timecode
        };

        private readonly IProcessProvider _processProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public FfmpegMatroskaRemuxer(IProcessProvider processProvider, IDiskProvider diskProvider, Logger logger)
        {
            _processProvider = processProvider;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public AudioTrackRemuxStreamMap Remux(string sourcePath, string targetPath, string sourceRawStreamData, IReadOnlyList<AudioTrackRetagTrack> edits, TimeSpan timeout)
        {
            var map = BuildStreamMap(sourceRawStreamData);

            if (map.Dropped.Any())
            {
                _logger.Info("Remux of '{0}': {1} stream(s) cannot be carried into Matroska and will be dropped: {2}", sourcePath, map.Dropped.Count, string.Join(", ", map.Dropped));
            }

            var binary = ResolveBinary();
            var args = BuildArguments(sourcePath, targetPath, map, edits);
            var tail = new Queue<string>();

            _logger.Debug("Remuxing to Matroska: {0} {1}", binary, args);

            var process = _processProvider.Start(binary,
                                                 args,
                                                 null,
                                                 line => _logger.Trace("ffmpeg: {0}", line),
                                                 line =>
                                                 {
                                                     _logger.Trace("ffmpeg: {0}", line);

                                                     lock (tail)
                                                     {
                                                         tail.Enqueue(line);

                                                         while (tail.Count > StderrTailLines)
                                                         {
                                                             tail.Dequeue();
                                                         }
                                                     }
                                                 });

            if (!process.WaitForExit((int)Math.Max(1000, timeout.TotalMilliseconds)))
            {
                _processProvider.Kill(process.Id);
                throw new AudioTrackRetagException($"ffmpeg did not finish remuxing '{sourcePath}' within {(int)timeout.TotalSeconds}s");
            }

            if (process.ExitCode != 0)
            {
                string stderr;

                lock (tail)
                {
                    stderr = string.Join(" | ", tail.Select(l => l.Trim()).Where(l => l.IsNotNullOrWhiteSpace()));
                }

                throw new AudioTrackRetagException($"ffmpeg exited with code {process.ExitCode} while remuxing '{sourcePath}': {stderr}");
            }

            return map;
        }

        /// <summary>
        /// Decides which streams go into the Matroska output from the ffprobe stream JSON MediaInfo
        /// already holds. Video, audio, subtitle and attachment streams are kept in source order
        /// unless their codec is known to have no Matroska mapping; data streams are never kept.
        /// Without usable JSON every stream is mapped (-map 0 -ignore_unknown) and ffmpeg decides.
        /// </summary>
        public static AudioTrackRemuxStreamMap BuildStreamMap(string rawStreamData)
        {
            var map = new AudioTrackRemuxStreamMap();

            if (rawStreamData.IsNullOrWhiteSpace())
            {
                map.MapAll = true;
                return map;
            }

            try
            {
                using var document = JsonDocument.Parse(rawStreamData);

                if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                {
                    map.MapAll = true;
                    return map;
                }

                foreach (var stream in streams.EnumerateArray())
                {
                    var index = stream.TryGetProperty("index", out var i) && i.TryGetInt32(out var parsed) ? parsed : map.Kept.Count + map.Dropped.Count;
                    var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                    var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() : null;

                    var carriable = type is "video" or "audio" or "subtitle" or "attachment";

                    if (carriable && (codec.IsNullOrWhiteSpace() || !UnsupportedInMatroska.Contains(codec)))
                    {
                        map.Kept.Add(index);
                    }
                    else
                    {
                        map.Dropped.Add($"#{index} {type ?? "unknown"} {codec ?? "unknown"}");
                    }
                }

                if (!map.Kept.Any())
                {
                    // nothing recognisable: let ffmpeg decide rather than produce an empty file
                    map.MapAll = true;
                    map.Dropped.Clear();
                }
            }
            catch (JsonException)
            {
                map.MapAll = true;
            }

            return map;
        }

        /// <summary>
        /// Plain argument list (no shell). Streams are mapped explicitly and copied; the audio language of
        /// each edited track is set on the output stream (output audio order equals source audio order
        /// because every audio stream is kept, so the record's audio-relative index applies unchanged).
        /// </summary>
        public static string BuildArguments(string sourcePath, string targetPath, AudioTrackRemuxStreamMap map, IReadOnlyList<AudioTrackRetagTrack> edits)
        {
            var parts = new List<string> { "-y", "-nostdin", "-loglevel", "warning", "-fflags", "+genpts", "-i", Quote(sourcePath) };

            if (map.MapAll)
            {
                parts.Add("-map");
                parts.Add("0");
                parts.Add("-ignore_unknown");
            }
            else
            {
                foreach (var index in map.Kept)
                {
                    parts.Add("-map");
                    parts.Add($"0:{index}");
                }
            }

            parts.Add("-c");
            parts.Add("copy");

            foreach (var edit in (edits ?? Array.Empty<AudioTrackRetagTrack>()).OrderBy(e => e.StreamIndex))
            {
                parts.Add($"-metadata:s:a:{edit.StreamIndex}");
                parts.Add($"language={edit.To}");
            }

            parts.Add("-f");
            parts.Add("matroska");
            parts.Add(Quote(targetPath));

            return string.Join(" ", parts);
        }

        /// <summary>Number of streams in an ffprobe stream JSON document, or -1 when it cannot be read.</summary>
        public static int CountStreams(string rawStreamData)
        {
            if (rawStreamData.IsNullOrWhiteSpace())
            {
                return -1;
            }

            try
            {
                using var document = JsonDocument.Parse(rawStreamData);

                if (document.RootElement.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
                {
                    return streams.GetArrayLength();
                }
            }
            catch (JsonException)
            {
            }

            return -1;
        }

        /// <summary>The bundled ffmpeg next to the Sonarr binary when present (the image symlinks it there), else PATH; same rule as the clip extractor.</summary>
        public string ResolveBinary()
        {
            var name = OsInfo.IsWindows ? "ffmpeg.exe" : "ffmpeg";
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);

            return _diskProvider.FileExists(bundled) ? bundled : name;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
