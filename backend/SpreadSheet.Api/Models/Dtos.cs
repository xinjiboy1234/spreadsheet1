using System.Text.Json;

namespace SpreadSheet.Api.Models;

public class CreateDocumentRequest
{
    public string? Title { get; set; }
    public string WorkbookJson { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public class UpdateDocumentRequest
{
    public string WorkbookJson { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Remark { get; set; }
}

public class FillSaveRequest
{
    public JsonElement Data { get; set; }
    public string? Title { get; set; }
}

public class DocumentListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public int CurrentVersionNo { get; set; }
}

public class DocumentDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid CurrentVersionId { get; set; }
    public int VersionNo { get; set; }
    public string WorkbookJson { get; set; } = string.Empty;
    public TemplateSchema? Schema { get; set; }
}

public class DocumentVersionSummaryDto
{
    public Guid Id { get; set; }
    public int VersionNo { get; set; }
    public string? Remark { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class DocumentVersionDetailDto
{
    public Guid Id { get; set; }
    public int VersionNo { get; set; }
    public string WorkbookJson { get; set; } = string.Empty;
    public TemplateSchema? Schema { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Remark { get; set; }
}

public class FillResponseDto
{
    public string WorkbookJson { get; set; } = string.Empty;
    public TemplateSchema? Schema { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class FillSaveResponseDto
{
    public Guid Id { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
}
