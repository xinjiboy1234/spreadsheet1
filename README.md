# SpreadSheet (UniverSheet)

Vue 3 + ASP.NET Core app for WYSIWYG Excel editing, version history, and template fill (simple fields + single-row loops).

Workbook snapshots are stored as Univer `IWorkbookData` JSON in SQLite. Excel import/export runs **client-side** via **LuckyExcel** (`@mertdeveci55/univer-import-export`) — not the official Univer Pro exchange / collaboration server.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)

## Quick start

### API (port 5050)

```bash
dotnet run --project backend/SpreadSheet.Api
```

Base URL: `http://localhost:5050`

On first start, the API seeds **销售订单模板** (`11111111-1111-1111-1111-111111111111`) if it is not already present.

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Vite proxies `/api` to the API. Open the URL printed by Vite (typically `http://localhost:5173`).

## Features

- Edit workbooks in Univer (styles, cells, sheets)
- Import / export `.xlsx` with WYSIWYG style round-trip (client-side)
- Document list, create, save, delete
- Version history (browse older snapshots; continue editing creates a new version)
- Template fill: scan placeholders, fill from JSON, warnings for missing fields / bad loops

## Placeholder syntax

| Kind | Syntax |
|------|--------|
| Simple field | `{{Field}}` |
| Loop start | `{{#List}}` |
| Loop field | `{{List.Field}}` |
| Loop end | `{{/List}}` |

Example fill JSON:

```json
{
  "CustomerName": "张三公司",
  "OrderDate": "2026-09-15",
  "Items": [
    { "Name": "零件A", "Qty": 2, "Amount": 100 },
    { "Name": "零件B", "Qty": 5, "Amount": 250 }
  ]
}
```

## Pinned dependency versions

From `frontend/package.json` and `backend/SpreadSheet.Api/SpreadSheet.Api.csproj`:

| Package | Version |
|---------|---------|
| `@univerjs/presets` | `^0.25.1` |
| `@univerjs/preset-sheets-core` | `^0.25.1` |
| `@mertdeveci55/univer-import-export` | `^0.2.1` |
| `Microsoft.EntityFrameworkCore.Sqlite` | `8.0.31` |
| `Microsoft.EntityFrameworkCore.Design` | `8.0.31` |
| Target framework | `net8.0` |

## Project layout

```
SpreadSheet/
  backend/
    SpreadSheet.Api/          # Web API, EF Core + SQLite, fill/scan services, seed
    SpreadSheet.Api.Tests/    # xUnit
  frontend/                   # Vue 3 + Vite + Univer host + excel I/O
  docs/superpowers/           # design spec + implementation plan
```

- `TemplateScanner` / `FillEngine`: pure C#, unit-tested
- `DocumentService`: persistence and versions
- `UniverSheetHost.vue`: Univer lifecycle + snapshot get/set
- `excelIo.ts`: only module that touches xlsx bytes

## Spec §9 verification checklist

Automated (run from repo root):

```bash
dotnet test backend/SpreadSheet.sln
cd frontend && npm run build
```

Optional smoke: start the API, then `GET http://localhost:5050/api/documents` — response should include **销售订单模板** (on a fresh DB or when seed id is present).

Manual QA (design spec §9):

1. Blank create → style edit → save → reopen — styles OK
2. Import styled xlsx → export → Excel keeps styles
3. Save 3× → versions list has 3 → open v1 → save → v4 appears
4. Seed + sample JSON → loop row count OK → fields replaced → export opens in Excel
5. Missing field / bad loop markers → HTTP 200 + `warnings`
