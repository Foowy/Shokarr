using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Shokarr.Controllers;

/// <summary>Shared route/versioning setup and response envelope for all Shokarr API controllers.</summary>
[ApiController]
[Authorize("admin")]
[ApiVersion(ShokarrConstants.ApiVersion)]
[Route("/api/v{version:apiVersion}/Shokarr/[controller]")]
public abstract class ShokarrBaseController : ControllerBase
{
    /// <summary>Standard response envelope for Shokarr API endpoints.</summary>
    /// <param name="Success">Whether the operation succeeded.</param>
    /// <param name="Message">An optional error or status message.</param>
    /// <param name="Data">The result payload.</param>
    public record ApiResponse<T>(bool Success, string? Message, T? Data);
}
