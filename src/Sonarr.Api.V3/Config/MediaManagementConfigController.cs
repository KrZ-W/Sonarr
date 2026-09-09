using FluentValidation;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using Sonarr.Http;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/mediamanagement")]
    public class MediaManagementConfigController : ConfigController<MediaManagementConfigResource>
    {
        public MediaManagementConfigController(IConfigService configService,
                                           PathExistsValidator pathExistsValidator,
                                           FolderChmodValidator folderChmodValidator,
                                           FolderWritableValidator folderWritableValidator,
                                           SeriesPathValidator seriesPathValidator,
                                           StartupFolderValidator startupFolderValidator,
                                           SystemFolderValidator systemFolderValidator,
                                           RootFolderAncestorValidator rootFolderAncestorValidator,
                                           RootFolderValidator rootFolderValidator)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.RecycleBinCleanupDays).GreaterThanOrEqualTo(0);
            SharedValidator.RuleFor(c => c.ChmodFolder).SetValidator(folderChmodValidator).When(c => !string.IsNullOrEmpty(c.ChmodFolder) && (OsInfo.IsLinux || OsInfo.IsOsx));

            SharedValidator.RuleFor(c => c.RecycleBin).IsValidPath()
                                                      .SetValidator(folderWritableValidator)
                                                      .SetValidator(rootFolderValidator)
                                                      .SetValidator(pathExistsValidator)
                                                      .SetValidator(rootFolderAncestorValidator)
                                                      .SetValidator(startupFolderValidator)
                                                      .SetValidator(systemFolderValidator)
                                                      .SetValidator(seriesPathValidator)
                                                      .When(c => !string.IsNullOrWhiteSpace(c.RecycleBin));

            SharedValidator.RuleFor(c => c.ScriptImportPath).IsValidPath().When(c => c.UseScriptImport);

            SharedValidator.RuleFor(c => c.MinimumFreeSpaceWhenImporting).GreaterThanOrEqualTo(100);

            // krzw(audio-language-verification)
            SharedValidator.RuleFor(c => c.AudioLanguageVerificationEndpoint).IsValidUrl().When(c => c.AudioLanguageVerificationEnabled);
            SharedValidator.RuleFor(c => c.AudioLanguageVerificationConfidenceThreshold).InclusiveBetween(0, 1);
            SharedValidator.RuleFor(c => c.AudioLanguageVerificationClipOffset).GreaterThanOrEqualTo(0);
            SharedValidator.RuleFor(c => c.AudioLanguageVerificationClipLength).InclusiveBetween(1, 600);
            SharedValidator.RuleFor(c => c.AudioLanguageVerificationTimeout).InclusiveBetween(1, 3600);
        }

        protected override MediaManagementConfigResource ToResource(IConfigService model)
        {
            return MediaManagementConfigResourceMapper.ToResource(model);
        }
    }
}
