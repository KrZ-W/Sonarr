using System;
using System.Collections.Generic;
using System.Linq;
using FFMpegCore;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.MediaFiles.AudioLanguage
{
    public interface IAudioTrackLayoutReader
    {
        /// <summary>Per-track layout from the ffprobe JSON MediaInfo already holds. Empty when unavailable.</summary>
        List<AudioTrackInfo> Read(MediaInfoModel mediaInfo);
    }

    public class FfprobeAudioTrackLayoutReader : IAudioTrackLayoutReader
    {
        private readonly Logger _logger;

        public FfprobeAudioTrackLayoutReader(Logger logger)
        {
            _logger = logger;
        }

        public List<AudioTrackInfo> Read(MediaInfoModel mediaInfo)
        {
            if (mediaInfo?.RawStreamData.IsNullOrWhiteSpace() ?? true)
            {
                return new List<AudioTrackInfo>();
            }

            try
            {
                var analysis = FFProbe.AnalyseStreamJson(mediaInfo.RawStreamData);

                return analysis.AudioStreams
                    .OrderBy(s => s.Index)
                    .Select((stream, audioIndex) => new AudioTrackInfo
                    {
                        StreamIndex = audioIndex,
                        Codec = stream.CodecName,
                        Channels = stream.Channels,
                        Language = stream.Language,
                        Title = GetTitle(stream)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to read the audio track layout from media info");
                return new List<AudioTrackInfo>();
            }
        }

        private static string GetTitle(MediaStream stream)
        {
            if (stream.Tags == null)
            {
                return null;
            }

            if (stream.Tags.TryGetValue("title", out var title) && title.IsNotNullOrWhiteSpace())
            {
                return title;
            }

            if (stream.Tags.TryGetValue("TITLE", out var upper) && upper.IsNotNullOrWhiteSpace())
            {
                return upper;
            }

            return null;
        }
    }
}
