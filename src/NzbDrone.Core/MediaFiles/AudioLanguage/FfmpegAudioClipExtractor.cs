using System;
using System.Globalization;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public class FfmpegAudioClipExtractor : IAudioClipExtractor
    {
        private readonly IProcessProvider _processProvider;
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public FfmpegAudioClipExtractor(IProcessProvider processProvider,
                                        IDiskProvider diskProvider,
                                        IAppFolderInfo appFolderInfo,
                                        Logger logger)
        {
            _processProvider = processProvider;
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        public byte[] Extract(string path, int audioStreamIndex, TimeSpan offset, TimeSpan length, TimeSpan timeout)
        {
            var ffmpeg = ResolveBinary();
            var clipPath = Path.Combine(_appFolderInfo.TempFolder, $"sonarr-audio-language-{Guid.NewGuid():N}.wav");

            try
            {
                var args = BuildArguments(path, audioStreamIndex, offset, length, clipPath);
                _logger.Trace("Extracting audio clip: {0} {1}", ffmpeg, args);

                var process = _processProvider.Start(ffmpeg, args, null, null, line => _logger.Trace("ffmpeg: {0}", line));

                if (!process.WaitForExit((int)Math.Max(1000, timeout.TotalMilliseconds)))
                {
                    _logger.Warn("ffmpeg did not finish extracting a clip of '{0}' within {1}s, killing it", path, (int)timeout.TotalSeconds);
                    _processProvider.Kill(process.Id);
                    return null;
                }

                if (process.ExitCode != 0)
                {
                    _logger.Warn("ffmpeg exited with code {0} while extracting audio stream {1} of '{2}'", process.ExitCode, audioStreamIndex, path);
                    return null;
                }

                if (!_diskProvider.FileExists(clipPath))
                {
                    _logger.Warn("ffmpeg produced no clip for audio stream {0} of '{1}'", audioStreamIndex, path);
                    return null;
                }

                return File.ReadAllBytes(clipPath);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to extract an audio clip from '{0}'", path);
                return null;
            }
            finally
            {
                try
                {
                    if (_diskProvider.FileExists(clipPath))
                    {
                        _diskProvider.DeleteFile(clipPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to delete temporary clip {0}", clipPath);
                }
            }
        }

        /// <summary>Plain argument list (no shell): one mono 16 kHz PCM WAV of the requested audio stream.</summary>
        public static string BuildArguments(string path, int audioStreamIndex, TimeSpan offset, TimeSpan length, string clipPath)
        {
            var offsetSeconds = Math.Max(0, offset.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            var lengthSeconds = Math.Max(1, length.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);

            return $"-y -nostdin -loglevel error -ss {offsetSeconds} -t {lengthSeconds} -i {Quote(path)} -map 0:a:{audioStreamIndex} -vn -ac 1 -ar 16000 -c:a pcm_s16le {Quote(clipPath)}";
        }

        /// <summary>The bundled ffmpeg next to the Sonarr binary when present (the image symlinks it there), else PATH.</summary>
        public static string ResolveBinary()
        {
            var name = OsInfo.IsWindows ? "ffmpeg.exe" : "ffmpeg";
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);

            return File.Exists(bundled) ? bundled : name;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
