using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DataAugmentation.Scene;
using Sonarr.Http;

namespace Sonarr.Api.V3.SceneMappings
{
    // krzw(scene-mappings): bulk import of curated series titles; all logic lives in IUserSceneMappingImportService.
    [V3ApiController("scenemapping")]
    public class UserSceneMappingController : Controller
    {
        private readonly IUserSceneMappingImportService _importService;

        public UserSceneMappingController(IUserSceneMappingImportService importService)
        {
            _importService = importService;
        }

        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserSceneMappingImportSummaryResource ImportUserTitles([FromBody] List<UserSceneMappingImportResource> resources)
        {
            return _importService.Import(resources.ToImportRequests()).ToResource();
        }
    }
}
