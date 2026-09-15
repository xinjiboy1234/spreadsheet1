# UniverSheet WYSIWYG Editor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Vue 3 + ASP.NET Core product for WYSIWYG Excel editing, version history, and template fill (simple fields + single-row loops).

**Architecture:** Snapshot storage of Univer `IWorkbookData` JSON in SQLite; frontend owns xlsx import/export via `@mertdeveci55/univer-import-export` (official Univer exchange is Pro/server — out of scope); backend owns versioning, `TemplateScanner`, and `FillEngine`.

**Tech Stack:** Vue 3 + TypeScript + Vite + Vue Router + Axios; `@univerjs/presets` + `@univerjs/preset-sheets-core`; `@mertdeveci55/univer-import-export`; .NET 8 Web API + EF Core + SQLite + xUnit.

**Spec:** `docs/superpowers/specs/2026-09-15-univer-spreadsheet-design.md`

---

## File Structure

```
SpreadSheet/
  backend/SpreadSheet.Api/   # see tasks for exact files
  backend/SpreadSheet.Api.Tests/
  frontend/src/...
  README.md
  .gitignore
```

**Responsibility notes**
- `TemplateScanner` / `FillEngine`: pure C#, unit-tested
- `DocumentService`: persistence only
- `UniverSheetHost.vue`: Univer lifecycle + get/set snapshot
- `excelIo.ts`: only place that touches xlsx bytes
- `useEditorDirty.ts`: `templateDirty` / `previewDirty` per spec §5.2

**Minimal workbook fixture** (reuse in scanner/fill tests):

```json
{
  "id": "wb1",
  "sheetOrder": ["s1"],
  "sheets": {
    "s1": {
      "id": "s1",
      "name": "Sheet1",
      "cellData": {
        "0": { "0": { "v": "客户:{{CustomerName}}" } },
        "1": {
          "0": { "v": "{{#Items}}" },
          "1": { "v": "{{Items.Name}}" },
          "2": { "v": "{{Items.Qty}}" },
          "3": { "v": "{{/Items}}" }
        }
      }
    }
  }
}
```

**Sample fill JSON** (FillPanel button + tests):

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

---

## Chunk 1: Repo + Backend Core API

### Task 1: Scaffold repo ignore + backend solution

**Files:**
- Create: `.gitignore`
- Create: `backend/SpreadSheet.sln` via `dotnet new`

- [ ] **Step 1: Write `.gitignore`**

Include: `bin/`, `obj/`, `node_modules/`, `dist/`, `*.db`, `*.db-shm`, `*.db-wal`, `.vs/`, `.superpowers/`, `.env`

- [ ] **Step 2: Create solution**

```powershell
cd d:\files\projects\netcore\web\api\SpreadSheet
dotnet new sln -n SpreadSheet -o backend
dotnet new webapi -n SpreadSheet.Api -o backend/SpreadSheet.Api
dotnet new xunit -n SpreadSheet.Api.Tests -o backend/SpreadSheet.Api.Tests
dotnet sln backend/SpreadSheet.sln add backend/SpreadSheet.Api/SpreadSheet.Api.csproj
dotnet sln backend/SpreadSheet.sln add backend/SpreadSheet.Api.Tests/SpreadSheet.Api.Tests.csproj
dotnet add backend/SpreadSheet.Api.Tests reference backend/SpreadSheet.Api
dotnet add backend/SpreadSheet.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add backend/SpreadSheet.Api package Microsoft.EntityFrameworkCore.Design
dotnet add backend/SpreadSheet.Api.Tests package Microsoft.EntityFrameworkCore.Sqlite
```

Delete default WeatherForecast controller/models.

- [ ] **Step 3: Verify build** — `dotnet build backend/SpreadSheet.sln` → succeeded

- [ ] **Step 4: Commit** — `chore: scaffold ASP.NET Core API and test projects`

### Task 2: Entities + DbContext

**Files:**
- Create: `backend/SpreadSheet.Api/Data/Entities/Document.cs`
- Create: `backend/SpreadSheet.Api/Data/Entities/DocumentVersion.cs`
- Create: `backend/SpreadSheet.Api/Data/AppDbContext.cs`
- Modify: `backend/SpreadSheet.Api/Program.cs`

- [ ] **Step 1: Add entities** — Document (Id, Title, CreatedAt, UpdatedAt, CurrentVersionId); DocumentVersion (Id, DocumentId, VersionNo, Remark, WorkbookJson, TemplateSchemaJson, CreatedAt)

- [ ] **Step 2: AppDbContext** — 1→N Versions; CurrentVersionId without cascade cycle

- [ ] **Step 3: Program.cs** — SQLite `Data Source=spreadsheet.db`, CORS AllowAny, `EnsureCreated()`

- [ ] **Step 4: Commit** — `feat: add document and version EF entities`

### Task 3: TemplateScanner (TDD)

**Files:**
- Create: `backend/SpreadSheet.Api/Models/TemplateSchema.cs`
- Create: `backend/SpreadSheet.Api/Services/TemplateScanner.cs`
- Create: `backend/SpreadSheet.Api.Tests/TemplateScannerTests.cs`

Cell path: `sheets[id].cellData[row][col].v`; merge ranges at `sheets[id].mergeData` (array of `{ startRow, endRow, startColumn, endColumn }` if present). Rows **0-based**.

- [ ] **Step 1: Write failing tests**

1. Simple `{{CustomerName}}` → fields contains CustomerName  
2. Same-row `#Items` / `Items.*` / `/Items` → one loop startRow==endRow  
3. Mismatched `#Items` without `/Items` → warnings non-empty  
4. `#Items` on row 1 and `/Items` on row 3 → multi-row warning  
5. Nested `{{#Outer}}` containing `{{#Inner}}` → nested warning  
6. Loop row overlapping a mergeData range → merge-conflict warning  
7. Duplicate field mentions → fields unique

- [ ] **Step 2:** `dotnet test --filter TemplateScanner` → FAIL

- [ ] **Step 3: Implement** regex `\{\{\s*([#/]?)([A-Za-z_][A-Za-z0-9_]*)(?:\.([A-Za-z_][A-Za-z0-9_]*))?\s*\}\}`

- [ ] **Step 4:** tests PASS

- [ ] **Step 5: Commit** — `feat: add TemplateScanner with unit tests`

### Task 4: FillEngine (TDD)

**Files:**
- Create: `backend/SpreadSheet.Api/Services/FillEngine.cs`
- Create: `backend/SpreadSheet.Api.Tests/FillEngineTests.cs`

- [ ] **Step 1: Write failing tests**

1. Simple replace; missing field keeps placeholder + warning  
2. Loop item missing field → keep placeholder + warning  
3. Loop N=2 → insert 1 row below, both filled  
4. Loop N=0 / missing array → delete template row  
5. Schema loop with merge warning → skip loop + warning  
6. Nested loop in schema → skip + warning  
7. Two loops → process by startRow descending

- [ ] **Step 2:** tests FAIL

- [ ] **Step 3: Implement** on JsonNode; clone row; shift lower rows; strip `#`/`/` tokens

- [ ] **Step 4:** tests PASS

- [ ] **Step 5: Commit** — `feat: add FillEngine with unit tests`

### Task 5: DocumentService + Controller

**Files:**
- Create: `backend/SpreadSheet.Api/Models/Dtos.cs`
- Create: `backend/SpreadSheet.Api/Services/DocumentService.cs`
- Create: `backend/SpreadSheet.Api/Controllers/DocumentsController.cs`
- Create: `backend/SpreadSheet.Api.Tests/DocumentServiceTests.cs`
- Modify: `Program.cs` — `AddScoped` DocumentService, TemplateScanner, FillEngine

- [ ] **Step 1: DocumentServiceTests**

Cover: create requires workbookJson string; default title `未命名文档`; list; get; put bumps VersionNo + scans schema; GET versions (no workbook); GET version by id; GET schema; fill returns workbookJson + warnings without writing; **fill-save creates NEW document Id** with Version 1 = filled workbook and **re-scanned** schema; 404 missing doc.

- [ ] **Step 2:** tests FAIL

- [ ] **Step 3: Implement** all endpoints; errors `{ "message": "文档不存在" }` etc.

- [ ] **Step 4:** `dotnet test` PASS; smoke GET `/api/documents` → `[]`

- [ ] **Step 5: Commit** — `feat: document CRUD, versions, and fill APIs`

---

## Chunk 2: Frontend Editor + I/O + Versions

### Task 6: Scaffold Vue app + API client

**Files:** `frontend/**`

- [ ] **Step 1: Scaffold + deps**

```powershell
npm create vite@latest frontend -- --template vue-ts
cd frontend
npm install vue-router axios @univerjs/presets @univerjs/preset-sheets-core @mertdeveci55/univer-import-export react react-dom rxjs
```

- [ ] **Step 2: Vite proxy** `/api` → port from `backend/SpreadSheet.Api/Properties/launchSettings.json`

- [ ] **Step 3: Router paths** `/`, `/editor/:id`, `/editor/:id/versions`; stubs for three views; `http.ts` baseURL `/api`

- [ ] **Step 4: `documents.ts` CRUD stubs** — `list`, `create`, `get`, `save`, `listVersions`, `getVersion` (fill/schema in Task 11)

- [ ] **Step 5:** `npm run build` succeeds

- [ ] **Step 6: Commit** — `chore: scaffold Vue frontend with Univer deps`

### Task 7: UniverSheetHost + dirty state

**Files:**
- Create: `frontend/src/components/UniverSheetHost.vue`
- Create: `frontend/src/utils/emptyWorkbook.ts`
- Create: `frontend/src/composables/useEditorDirty.ts`

- [ ] **Step 1: emptyWorkbook()** object + `JSON.stringify` helper

- [ ] **Step 2: UniverSheetHost** — import preset CSS `@univerjs/preset-sheets-core/lib/index.css`; createUniver + zh-CN if available; expose `getWorkbookJson(): string`, `loadWorkbookJson(json: string)`; dispose on unmount; emit `change` via command listener or polling-on-save only (document choice in code comment)

- [ ] **Step 3: useEditorDirty** — while `previewDirty`, edits do NOT set `templateDirty`; `markPreview` / `clearPreview` / `markTemplateEdit` / `clearAll`

- [ ] **Step 4: Manual** — blank sheet visible with toolbar chrome

- [ ] **Step 5: Commit** — `feat: Univer host and dirty-state composable`

### Task 8: List + Editor save/load

**Files:**
- Create: `frontend/src/views/ListView.vue`
- Create: `frontend/src/views/EditorView.vue`
- Modify: `frontend/src/App.vue`, `frontend/src/styles/app.css`

- [ ] **Step 1: ListView** — 新建 → emptyWorkbook → POST → `/editor/:id`; 打开; 历史 link; 导入 button placeholder

- [ ] **Step 2: EditorView** — load via GET `/documents/:id` (unless `?version=`); toolbar: 返回列表、保存、导出占位、填充占位、历史; PUT on save; on success clear dirty flags

- [ ] **Step 3: sessionStorage draft** — key `draft:{id}`; **write only when PUT fails**; on editor mount if draft exists offer restore once then keep until success save clears it; successful load/save clears draft

- [ ] **Step 4: Manual** create → edit → save → refresh persists; 返回列表 works

- [ ] **Step 5: Commit** — `feat: document list and editor save/load`

### Task 9: Excel import/export (WYSIWYG)

**Files:**
- Create: `frontend/src/utils/excelIo.ts`
- Modify: `frontend/src/views/ListView.vue`, `EditorView.vue`

- [ ] **Step 1: excelIo.ts** — wrap LuckyExcel; always return/accept snapshot object; callers `JSON.stringify` for API

- [ ] **Step 2: Import** — accept `.xlsx`/`.xls` only; else `alert('请选择 Excel 文件')` and **do not** call API; success → POST documents with workbookJson string

- [ ] **Step 3: Export** — getWorkbookJson → parse → transformUniverToExcel → download

- [ ] **Step 4: Manual** styled xlsx round-trip

- [ ] **Step 5: Commit** — `feat: frontend Excel import/export via LuckyExcel`

### Task 10: Versions view

**Files:**
- Create: `frontend/src/views/VersionsView.vue`
- Modify: `frontend/src/router/index.ts`, `EditorView.vue`

- [ ] **Step 1: Add route** `/editor/:id/versions` → VersionsView

- [ ] **Step 2: VersionsView** — GET versions; click row → `/editor/:id?version={versionId}`

- [ ] **Step 3: EditorView** — if `version` query: load `GET .../versions/{versionId}` (not current GET); disable fill controls; on successful save `router.replace({ path: `/editor/${id}` })` (drop query)

- [ ] **Step 4: Manual** save 3× → versions shows 3 → open v1 → save → versions shows 4

- [ ] **Step 5: Commit** — `feat: version history browsing and continue-edit`

---

## Chunk 3: Fill UI + Seed + README

### Task 11: FillPanel + API client

**Files:**
- Modify: `frontend/src/api/documents.ts` — add `getSchema`, `fill`, `fillSave`
- Modify: `frontend/src/types/document.ts`
- Create: `frontend/src/components/FillPanel.vue`
- Modify: `frontend/src/views/EditorView.vue`

- [ ] **Step 1: API methods**

```ts
getSchema(id: string)
fill(id: string, data: object) // → { workbookJson, schema, warnings }
fillSave(id: string, body: { data: object, title?: string }) // → { id, warnings }
```

- [ ] **Step 2: FillPanel UI** — display schema JSON; textarea for data; button「填入样例」loads sample fill JSON above; 试填; 放弃试填; 另存为新文档; 导出当前编辑器

- [ ] **Step 3: EditorView wiring (spec §5.2)**

Before fill/fill-save: if `previewDirty` → no auto-PUT; else if `templateDirty` → PUT first.  
试填 success → `loadWorkbookJson` + `markPreview()`.  
放弃 → GET current + `clearPreview()`.  
fill-save success → `router.push(/editor/newId)`.  
`?version=` → hide/disable FillPanel.  
Toolbar 保存 when `previewDirty` → confirm「将把填充结果保存为新版本，模板占位符会丢失」then PUT + clearAll.

- [ ] **Step 4: Manual** — 试填 / 放弃 / 二次试填仍基于模板 / fill-save 跳转

- [ ] **Step 5: Commit** — `feat: template fill panel`

### Task 12: Seed sales order template

**Files:**
- Create: `backend/SpreadSheet.Api/Seed/sales-order-template.workbook.json`
- Create: `backend/SpreadSheet.Api/Seed/SeedData.cs`
- Modify: `backend/SpreadSheet.Api/Program.cs`

- [ ] **Step 1: Produce workbook JSON** — in UI create sheet with CustomerName, OrderDate, Items loop row → save → copy `workbookJson` from GET into `Seed/sales-order-template.workbook.json` (or hand-author using minimal fixture shape + styles)

- [ ] **Step 2: SeedData** — if no Document with Id `11111111-1111-1111-1111-111111111111`, insert title `销售订单模板` + Version 1 (scan schema). Call from Program after EnsureCreated.

- [ ] **Step 3: Verify** — restart API; list contains 销售订单模板

- [ ] **Step 4: E2E** — open seed → 样例 → 试填 → Items 两行 → 导出 xlsx opens; fields replaced

- [ ] **Step 5: Commit** — `feat: seed sales order template`

### Task 13: README + verification

**Files:**
- Create: `README.md`

- [ ] **Step 1: README sections** — Prerequisites (.NET 8, Node 20+); start API (`dotnet run --project backend/SpreadSheet.Api`); start frontend (`npm run dev`); features list; placeholder syntax; note LuckyExcel vs Univer Pro; pinned package versions from `package.json` / csproj

- [ ] **Step 2: Spec §9 checklist (all must pass)**

1. Blank create → style edit → save → reopen styles OK  
2. Import styled xlsx → export → Excel keeps styles  
3. Save 3× → versions 3 → open v1 → save → v4  
4. Seed + sample JSON → loop count OK → export opens  
5. Missing field / bad loop → 200 + warnings

- [ ] **Step 3: Commit** — `docs: add README with run instructions`

---

## Implementation Note

Official Univer Excel exchange requires Pro server. This plan uses `@mertdeveci55/univer-import-export` for client-side WYSIWYG I/O. If snapshot shape drifts, fix only in `excelIo.ts`.
