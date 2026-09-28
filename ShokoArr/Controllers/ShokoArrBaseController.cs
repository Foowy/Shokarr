using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShokoArr.Controllers;

/// <summary>Shared route/versioning setup and response envelope for all ShokoArr API controllers.</summary>
[ApiController]
[Authorize("admin")]
[ApiVersion(ShokoArrConstants.ApiVersion)]
[Route("/api/v{version:apiVersion}/ShokoSonarr/[controller]")]
public abstract class ShokoArrBaseController : ControllerBase
{
    /// <summary>Standard response envelope for ShokoArr API endpoints.</summary>
    /// <param name="Success">Whether the operation succeeded.</param>
    /// <param name="Message">An optional error or status message.</param>
    /// <param name="Data">The result payload.</param>
    public record ApiResponse<T>(bool Success, string? Message, T? Data);
}
