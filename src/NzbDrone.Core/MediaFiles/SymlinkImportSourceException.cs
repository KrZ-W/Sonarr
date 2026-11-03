using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MediaFiles
{
    // krzw(symlink-import-guard): thrown by the file mover when an import source turns out to be a symbolic link
    // at transfer time (the Mono disk provider would otherwise recreate the link in the library).
    public class SymlinkImportSourceException : NzbDroneException
    {
        public SymlinkImportSourceException(string message)
            : base(message)
        {
        }
    }
}
