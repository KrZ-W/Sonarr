using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    /// <summary>
    /// mkvpropedit wrapper: one "--edit track:aN --set language=xxx" per track, header-only,
    /// so no stream data is ever rewritten. Runs through the process provider with a plain
    /// argument list (never a shell) and a timeout.
    /// </summary>
    public class MkvPropEditTrackTagger : IAudioTrackTagger
    {
        private readonly IProcessProvider _processProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public MkvPropEditTrackTagger(IProcessProvider processProvider, IDiskProvider diskProvider, Logger logger)
        {
            _processProvider = processProvider;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void SetLanguages(string path, IReadOnlyList<AudioTrackRetagTrack> edits, TimeSpan timeout)
        {
            if (edits == null || edits.Count == 0)
            {
                return;
            }

            var binary = ResolveBinary();
            var args = BuildArguments(path, edits);
            var errors = new StringBuilder();

            _logger.Debug("Retagging audio tracks: {0} {1}", binary, args);

            var process = _processProvider.Start(binary,
                                                 args,
                                                 null,
                                                 line => _logger.Trace("mkvpropedit: {0}", line),
                                                 line =>
                                                 {
                                                     _logger.Trace("mkvpropedit: {0}", line);
                                                     errors.AppendLine(line);
                                                 });

            if (!process.WaitForExit((int)Math.Max(1000, timeout.TotalMilliseconds)))
            {
                _processProvider.Kill(process.Id);
                throw new AudioTrackRetagException($"mkvpropedit did not finish within {(int)timeout.TotalSeconds}s on '{path}'");
            }

            // exit code 1 = warnings only (the edit was written); 2 = error
            if (process.ExitCode >= 2)
            {
                throw new AudioTrackRetagException($"mkvpropedit exited with code {process.ExitCode} on '{path}': {errors.ToString().Trim()}");
            }

            if (process.ExitCode == 1)
            {
                _logger.Warn("mkvpropedit reported warnings on '{0}': {1}", path, errors.ToString().Trim());
            }
        }

        /// <summary>Plain argument list (no shell). mkvpropedit numbers audio tracks from 1, the record from 0.</summary>
        public static string BuildArguments(string path, IReadOnlyList<AudioTrackRetagTrack> edits)
        {
            var parts = new List<string> { Quote(path) };

            foreach (var edit in edits.OrderBy(e => e.StreamIndex))
            {
                parts.Add("--edit");
                parts.Add($"track:a{edit.StreamIndex + 1}");
                parts.Add("--set");
                parts.Add($"language={edit.To}");
            }

            return string.Join(" ", parts);
        }

        /// <summary>The bundled mkvpropedit next to the Sonarr binary when present (the image symlinks it there, like ffmpeg), else PATH.</summary>
        public string ResolveBinary()
        {
            var name = OsInfo.IsWindows ? "mkvpropedit.exe" : "mkvpropedit";
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);

            return _diskProvider.FileExists(bundled) ? bundled : name;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    public class AudioTrackRetagException : Exception
    {
        public AudioTrackRetagException(string message)
            : base(message)
        {
        }

        public AudioTrackRetagException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
