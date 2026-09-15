namespace SpreadSheet.Api.Data.Entities;

public class DocumentVersion
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int VersionNo { get; set; }
    public string? Remark { get; set; }
    public string WorkbookJson { get; set; } = string.Empty;
    public string TemplateSchemaJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public Document Document { get; set; } = null!;
}
