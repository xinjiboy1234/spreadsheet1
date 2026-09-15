using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SpreadSheet.Api.Models;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documents;

    public DocumentsController(DocumentService documents)
    {
        _documents = documents;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentListItemDto>>> List(CancellationToken ct)
    {
        return Ok(await _documents.ListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDetailDto>> Create([FromBody] CreateDocumentRequest request, CancellationToken ct)
    {
        try
        {
            var doc = await _documents.CreateAsync(request, ct);
            return Ok(doc);
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDetailDto>> Get(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.GetAsync(id, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentDetailDto>> Update(Guid id, [FromBody] UpdateDocumentRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.UpdateAsync(id, request, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<List<DocumentVersionSummaryDto>>> ListVersions(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.ListVersionsAsync(id, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    public async Task<ActionResult<DocumentVersionDetailDto>> GetVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.GetVersionAsync(id, versionId, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/schema")]
    public async Task<ActionResult<TemplateSchema>> GetSchema(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.GetSchemaAsync(id, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/fill")]
    public async Task<ActionResult<FillResponseDto>> Fill(Guid id, [FromBody] JsonElement data, CancellationToken ct)
    {
        try
        {
            if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or not JsonValueKind.Object)
                return BadRequest(new ErrorResponse { Message = "填充数据无效" });

            return Ok(await _documents.FillAsync(id, data, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/fill-save")]
    public async Task<ActionResult<FillSaveResponseDto>> FillSave(Guid id, [FromBody] FillSaveRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _documents.FillSaveAsync(id, request, ct));
        }
        catch (DocumentServiceException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message });
        }
    }
}
