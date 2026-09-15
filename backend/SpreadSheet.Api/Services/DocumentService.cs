using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpreadSheet.Api.Data;
using SpreadSheet.Api.Data.Entities;
using SpreadSheet.Api.Models;

namespace SpreadSheet.Api.Services;

public class DocumentServiceException : Exception
{
    public int StatusCode { get; }

    public DocumentServiceException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class DocumentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _db;
    private readonly TemplateScanner _scanner;
    private readonly FillEngine _fillEngine;

    public DocumentService(AppDbContext db, TemplateScanner scanner, FillEngine fillEngine)
    {
        _db = db;
        _scanner = scanner;
        _fillEngine = fillEngine;
    }

    public async Task<List<DocumentListItemDto>> ListAsync(CancellationToken ct = default)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .Include(d => d.CurrentVersion)
            .ToListAsync(ct);

        return docs
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new DocumentListItemDto
            {
                Id = d.Id,
                Title = d.Title,
                UpdatedAt = d.UpdatedAt,
                CurrentVersionNo = d.CurrentVersion?.VersionNo ?? 0
            }).ToList();
    }

    public async Task<DocumentDetailDto> CreateAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        RequireWorkbookJson(request.WorkbookJson);
        var schema = ScanUserWorkbook(request.WorkbookJson);
        var schemaJson = SerializeSchema(schema);
        var now = DateTimeOffset.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(request.Title) ? "未命名文档" : request.Title.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CurrentVersionId = null
        };

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            VersionNo = 1,
            Remark = request.Remark,
            WorkbookJson = request.WorkbookJson,
            TemplateSchemaJson = schemaJson,
            CreatedAt = now
        };

        _db.Documents.Add(doc);
        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        doc.CurrentVersionId = version.Id;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDetail(doc, version, schema);
    }

    public async Task<DocumentDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await LoadDocumentWithCurrentAsync(id, ct);
        var version = doc.CurrentVersion!;
        return ToDetail(doc, version, DeserializeSchema(version.TemplateSchemaJson));
    }

    public async Task<DocumentDetailDto> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct = default)
    {
        RequireWorkbookJson(request.WorkbookJson);
        var schema = ScanUserWorkbook(request.WorkbookJson);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var doc = await LoadDocumentWithCurrentAsync(id, ct);
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Title))
            doc.Title = request.Title.Trim();

        var next = await _db.DocumentVersions
            .Where(v => v.DocumentId == id)
            .MaxAsync(v => (int?)v.VersionNo, ct) ?? 0;

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            VersionNo = next + 1,
            Remark = request.Remark,
            WorkbookJson = request.WorkbookJson,
            TemplateSchemaJson = SerializeSchema(schema),
            CreatedAt = now
        };

        doc.CurrentVersionId = version.Id;
        doc.UpdatedAt = now;
        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDetail(doc, version, schema);
    }

    public async Task<List<DocumentVersionSummaryDto>> ListVersionsAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureDocumentExistsAsync(id, ct);

        return await _db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.DocumentId == id)
            .OrderByDescending(v => v.VersionNo)
            .Select(v => new DocumentVersionSummaryDto
            {
                Id = v.Id,
                VersionNo = v.VersionNo,
                Remark = v.Remark,
                CreatedAt = v.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<DocumentVersionDetailDto> GetVersionAsync(Guid id, Guid versionId, CancellationToken ct = default)
    {
        await EnsureDocumentExistsAsync(id, ct);

        var version = await _db.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.DocumentId == id && v.Id == versionId, ct);

        if (version is null)
            throw new DocumentServiceException(404, "文档不存在");

        return new DocumentVersionDetailDto
        {
            Id = version.Id,
            VersionNo = version.VersionNo,
            WorkbookJson = version.WorkbookJson,
            Schema = DeserializeSchema(version.TemplateSchemaJson),
            CreatedAt = version.CreatedAt,
            Remark = version.Remark
        };
    }

    public async Task<TemplateSchema> GetSchemaAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await LoadDocumentWithCurrentAsync(id, ct);
        return DeserializeSchema(doc.CurrentVersion!.TemplateSchemaJson);
    }

    public async Task<FillResponseDto> FillAsync(Guid id, JsonElement data, CancellationToken ct = default)
    {
        RequireFillDataObject(data);

        var doc = await LoadDocumentWithCurrentAsync(id, ct);
        var version = doc.CurrentVersion!;
        var schema = DeserializeSchema(version.TemplateSchemaJson);
        var fill = _fillEngine.Fill(version.WorkbookJson, data, schema);

        return new FillResponseDto
        {
            WorkbookJson = fill.WorkbookJson,
            Schema = schema,
            Warnings = fill.Warnings
        };
    }

    public async Task<FillSaveResponseDto> FillSaveAsync(Guid id, FillSaveRequest request, CancellationToken ct = default)
    {
        RequireFillDataObject(request.Data);

        var source = await LoadDocumentWithCurrentAsync(id, ct);
        var version = source.CurrentVersion!;
        var preSchema = DeserializeSchema(version.TemplateSchemaJson);

        var fill = _fillEngine.Fill(version.WorkbookJson, request.Data, preSchema);
        var rescanned = _scanner.Scan(fill.WorkbookJson);
        var now = DateTimeOffset.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var newDoc = new Document
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(request.Title)
                ? $"{source.Title}-填充"
                : request.Title.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            CurrentVersionId = null
        };

        var newVersion = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = newDoc.Id,
            VersionNo = 1,
            Remark = null,
            WorkbookJson = fill.WorkbookJson,
            TemplateSchemaJson = SerializeSchema(rescanned),
            CreatedAt = now
        };

        _db.Documents.Add(newDoc);
        _db.DocumentVersions.Add(newVersion);
        await _db.SaveChangesAsync(ct);

        newDoc.CurrentVersionId = newVersion.Id;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new FillSaveResponseDto
        {
            Id = newDoc.Id,
            Warnings = fill.Warnings
        };
    }

    private async Task EnsureDocumentExistsAsync(Guid id, CancellationToken ct)
    {
        var exists = await _db.Documents.AsNoTracking().AnyAsync(d => d.Id == id, ct);
        if (!exists)
            throw new DocumentServiceException(404, "文档不存在");
    }

    private async Task<Document> LoadDocumentWithCurrentAsync(Guid id, CancellationToken ct)
    {
        var doc = await _db.Documents
            .Include(d => d.CurrentVersion)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (doc is null || doc.CurrentVersion is null)
            throw new DocumentServiceException(404, "文档不存在");

        return doc;
    }

    private TemplateSchema ScanUserWorkbook(string workbookJson)
    {
        try
        {
            return _scanner.Scan(workbookJson);
        }
        catch (JsonException)
        {
            throw new DocumentServiceException(400, "workbookJson 不是合法 JSON");
        }
    }

    private static void RequireWorkbookJson(string? workbookJson)
    {
        if (string.IsNullOrWhiteSpace(workbookJson))
            throw new DocumentServiceException(400, "workbookJson 不能为空");
    }

    private static void RequireFillDataObject(JsonElement data)
    {
        if (data.ValueKind is not JsonValueKind.Object)
            throw new DocumentServiceException(400, "填充数据无效");
    }

    private static string SerializeSchema(TemplateSchema schema) =>
        JsonSerializer.Serialize(schema, JsonOptions);

    private static TemplateSchema DeserializeSchema(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new TemplateSchema();
        return JsonSerializer.Deserialize<TemplateSchema>(json, JsonOptions) ?? new TemplateSchema();
    }

    private static DocumentDetailDto ToDetail(Document doc, DocumentVersion version, TemplateSchema schema) =>
        new()
        {
            Id = doc.Id,
            Title = doc.Title,
            CurrentVersionId = version.Id,
            VersionNo = version.VersionNo,
            WorkbookJson = version.WorkbookJson,
            Schema = schema
        };
}
