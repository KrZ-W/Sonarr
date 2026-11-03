using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport
{
    public class ImportDecision
    {
        private readonly List<ImportRejection> _rejections;  // krzw(symlink-import-guard)

        public LocalEpisode LocalEpisode { get; private set; }

        public IEnumerable<ImportRejection> Rejections => _rejections;

        public bool Approved => Rejections.Empty();

        public ImportDecision(LocalEpisode localEpisode, params ImportRejection[] rejections)
        {
            LocalEpisode = localEpisode;
            _rejections = rejections.ToList();
        }

        // krzw(symlink-import-guard): lets the import funnel turn an approved decision into a rejected one
        // when a late check (made right before the file is touched) fails.
        public void Reject(ImportRejection rejection)
        {
            _rejections.Add(rejection);
        }
    }
}
