using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles.AudioTags
{
    public interface IAudioTrackTagger
    {
        /// <summary>Rewrites the language tag of the given audio tracks in place (header-only edit). Throws on failure.</summary>
        void SetLanguages(string path, IReadOnlyList<AudioTrackRetagTrack> edits, TimeSpan timeout);
    }
}
