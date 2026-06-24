using System.Linq;

namespace NzbDrone.Core.CustomFormats
{
    public class AudioTitleSpecification : RegexSpecificationBase
    {
        public override int Order => 11;
        public override string ImplementationName => "Audio Title";
        public override string InfoLink => "https://wiki.servarr.com/sonarr/settings#custom-formats-2";

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return input.AudioTitles?.Any(MatchString) ?? false;
        }
    }
}
