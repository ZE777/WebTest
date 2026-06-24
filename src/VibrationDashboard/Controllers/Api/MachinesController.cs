using Microsoft.AspNetCore.Mvc;
using VibrationDashboard.Common.Exceptions;
using VibrationDashboard.DTOs.Responses;
using VibrationDashboard.Services.Machines;

namespace VibrationDashboard.Controllers.Api;

/// <summary>
/// 設備 REST API(薄層):接請求 → 呼叫 <see cref="IMachineService"/> → 回 DTO。
/// 業務錯誤由 Service 拋例外、Middleware 統一轉 ProblemDetails,本層不寫 try/catch。
/// </summary>
[ApiController]
[Route("api/machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly IMachineService _service;

    public MachinesController(IMachineService service) => _service = service;

    /// <summary>取得所有設備摘要(危險優先,不含圖檔)。</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MachineSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MachineSummaryDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetSummariesAsync(ct));

    /// <summary>取得單一設備明細(含量測序列)。</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<MachineDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MachineDetailDto>> GetById(string id, CancellationToken ct)
        => Ok(await _service.GetDetailAsync(id, ct));

    /// <summary>
    /// 取得設備圖檔(二位元)。支援 <c>ETag</c> 條件式請求:
    /// 帶 <c>If-None-Match</c> 且命中時回 <b>304 Not Modified</b>,不重傳內容。
    /// </summary>
    [HttpGet("{id}/image")]
    [Produces("image/png", "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(string id, CancellationToken ct)
    {
        var image = await _service.GetImageAsync(id, ct)
            ?? throw new NotFoundException("Machine", id);

        // 條件式請求:If-None-Match 命中目前 ETag → 304(省頻寬)。
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == image.ETag)
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.ETag = image.ETag;
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
        return File(image.Bytes, image.ContentType);
    }
}
