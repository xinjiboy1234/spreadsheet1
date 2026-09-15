# UniverSheet 所见即所得 Excel 编辑器 — 设计规格

**日期:** 2026-09-15  
**状态:** 已确认  
**范围:** 成品交付（非 MVP）

## 1. 目标与成功标准

构建基于 Univer Sheets 的所见即所得 Excel 编辑器，支持：

1. 加载带样式的 Excel（`.xlsx`）并编辑内容与样式
2. 空白文档直接开始编辑
3. 导出 Excel，导出结果与编辑器视觉一致（WYSIWYG）
4. 查看编辑记录（版本），点击某版本可继续编辑
5. 模板填充：简单字段替换 + 循环动态行，带演示数据闭环

**成功标准**

- 导入含字体/边框/填充色/合并单元格的 xlsx 后，编辑器内样式可见；**用户导出**后用 Excel 打开，上述样式仍保留
- 每次保存产生可回溯版本；从历史版本打开再保存会追加新版本
- 使用 `{{Field}}` / `{{#List}}` 约定可完成试填；试填结果在编辑器内预览后，再经 Univer 导出为 xlsx
- 无登录；本地启动前后端即可完整体验

## 2. 约束与明确不做

| 约束 | 选择 |
|------|------|
| 前端 | Vue 3 + TypeScript + Vite |
| 后端 | ASP.NET Core (.NET 8) Web API |
| 数据库 | SQLite + EF Core |
| 鉴权 | 无（单机/内网共用文档列表） |
| 占位符 | 单元格文本约定（非侧栏绑定元数据） |
| Univer | 官方 Sheets + 导入/导出 Excel 插件（实现时锁定当时稳定版，写入 README） |

**本次不做:** 登录/多租户、实时协作、公式引擎深度定制、版本差量压缩、对象存储、服务端 multipart 解析 xlsx、文档删除 API、多行循环块（仅支持单行模板行）、合并单元格横跨循环行的自动拆分。

## 3. 架构

```
Vue 3 (Univer Sheets)  --REST/JSON-->  ASP.NET Core Web API  --EF Core-->  SQLite
```

- **前端:** 文档列表、Univer 编辑器、版本历史、填充面板；负责 xlsx↔workbook 的导入/导出（WYSIWYG 唯一路径）与调用 API
- **后端:** 文档/版本持久化、TemplateSchema 扫描、填充引擎（只改 WorkbookJson）
- **真相源:** `WorkbookJson`（Univer workbook 序列化）

**导入 / 导出裁定（消除歧义）**

| 操作 | 唯一主路径 |
|------|------------|
| 导入 xlsx | 前端 Univer 导入插件 → `workbookJson` → `POST /documents`（带 json）建档 |
| 用户导出 xlsx（WYSIWYG） | 前端 Univer 导出插件，基于当前编辑器 workbook |
| 填充试填 | 后端 FillEngine 返回 `workbookJson` → 前端加载预览 → 用户再用 Univer 导出 |
| 填充直接下载 | 后端返回填充后的 `workbookJson`；**前端**立即用 Univer 导出为文件（不在服务端用 ClosedXML 生成用户下载文件） |

不在服务端维护 `XlsxBlob`；不引入 ClosedXML/NPOI 作为用户可见导出路径。

**仓库结构**

```
SpreadSheet/
  frontend/
    src/views/          # ListView, EditorView, VersionsView
    src/components/     # UniverSheetHost, FillPanel, ...
    src/api/
  backend/
    SpreadSheet.Api/
      Controllers/
      Services/         # DocumentService, TemplateScanner, FillEngine
      Data/             # DbContext, Entities
      Seed/             # 销售订单模板种子
  docs/superpowers/specs/
  README.md
```

## 4. 数据模型

### 4.1 Documents

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | Guid | PK |
| Title | string | 文档标题 |
| CreatedAt | DateTimeOffset | 创建时间 |
| UpdatedAt | DateTimeOffset | 最后更新 |
| CurrentVersionId | Guid? | FK → 当前版本 |

### 4.2 DocumentVersions

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | Guid | PK |
| DocumentId | Guid | FK |
| VersionNo | int | 从 1 递增 |
| Remark | string? | 可选备注 |
| WorkbookJson | string | Univer workbook JSON（编辑态真相） |
| TemplateSchemaJson | string | 保存时扫描结果；无占位符时为 `{"fields":[],"loops":[],"warnings":[]}` |
| CreatedAt | DateTimeOffset | 保存时间 |

**Schema 扫描时机（裁定）**

- 每次创建 Version 时（`POST /documents` 带 workbook、`PUT` 保存）同步调用 TemplateScanner，结果写入该 Version 的 `TemplateSchemaJson`
- `GET /documents/{id}/schema` 返回 **当前版本** 已持久化的 schema，不现场重扫
- 打开历史版本编辑时，schema 以该版本存库值为准；再保存时对新 Version 重新扫描

**版本行为**

- 保存 → 插入新 Version（VersionNo = 事务内 max+1），更新 Document.CurrentVersionId / UpdatedAt / Title（若传入）
- 打开历史版本 → 加载该 Version 的 WorkbookJson；再保存追加新 Version，不覆盖历史

### 4.3 TemplateSchema

```json
{
  "fields": ["CustomerName", "OrderDate"],
  "loops": [
    {
      "name": "Items",
      "sheet": "Sheet1",
      "startRow": 5,
      "endRow": 5,
      "fields": ["Name", "Qty", "Amount"]
    }
  ],
  "warnings": []
}
```

`warnings`：扫描期问题（如 `#Items` 无对应 `/Items`），不阻断保存。

### 4.4 占位符约定

| 类型 | 语法 | 示例 |
|------|------|------|
| 简单字段 | `{{FieldName}}` | `{{CustomerName}}` |
| 循环开始 | `{{#ListName}}` | 与模板行同行 |
| 循环结束 | `{{/ListName}}` | 与模板行同行 |
| 循环内字段 | `{{ListName.Field}}` | `{{Items.Name}}` |

**循环行规则（裁定）**

- 行列索引：**与 Univer 一致，0-based**（`startRow` / `endRow`）
- v1 **仅支持单行模板**（`startRow == endRow`）；多行块写入 schema.warnings 且填充时跳过该 loop
- 展开算法：模板行保留为第 1 条；若 N>1，在其下方插入 N−1 行并复制样式/单元格后写入其余项；若同 sheet 多个 loop，按 `startRow` **从大到小**处理以免行号错位
- 数组为空或缺少数组键 → **删除该模板行**
- 填充后去掉 `#`/`/` 标记文本
- 简单字段全表替换；请求未提供的字段 → 保留占位符原文，响应 `warnings` 增加一项
- 循环标记不匹配：保存成功，schema.warnings 记录；填充时 **跳过该 loop**（HTTP 200），响应 `warnings` 说明，不返回 400
- 合并单元格与循环行重叠：不做自动拆分；若检测到则 warnings，并跳过该 loop

### 4.5 填充请求体示例

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

## 5. API

基础路径: `/api`

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/documents` | `{ id, title, updatedAt, currentVersionNo }[]` |
| POST | `/documents` | 见下方建档 body |
| GET | `/documents/{id}` | `{ id, title, currentVersionId, versionNo, workbookJson, schema }` |
| PUT | `/documents/{id}` | `{ workbookJson, title?, remark? }` → 新 Version；返回同 GET 形状 |
| GET | `/documents/{id}/versions` | `{ id, versionNo, remark, createdAt }[]`（无 workbook） |
| GET | `/documents/{id}/versions/{versionId}` | `{ id, versionNo, workbookJson, schema, createdAt, remark }` |
| GET | `/documents/{id}/schema` | 当前版本 TemplateSchema 对象 |
| POST | `/documents/{id}/fill` | 试填，见下方响应 |
| POST | `/documents/{id}/fill-save` | 填充并另存，见下方 |

**无** `POST /documents/import` multipart；**无** `GET .../export` 服务端文件下载；**无** DELETE。

### 5.1 POST /documents（空白或导入建档）

```json
{
  "title": "可选标题",
  "workbookJson": "{...Univer workbook JSON string...}",
  "remark": null
}
```

- `workbookJson` **必填**（非空字符串）：作为 Version 1 存库并扫描 schema
- **新建空白:** 前端用 Univer 创建空表 → 取出 JSON → `POST /documents`
- **导入 xlsx:** 前端 Univer 解析 → 同上接口
- 缺少或空白 `workbookJson` → 400；后端不捏造 Univer 结构
- 响应：同 `GET /documents/{id}`

### 5.2 填充与编辑器状态（裁定）

- 服务端 fill / fill-save **只读已持久化的当前版本**（`CurrentVersionId`），不接受请求体中的临时 `workbookJson`，也不按 `?version=` 历史版本填充
- 前端维护两个脏标记：`templateDirty`（用户编辑模板未保存）、`previewDirty`（试填结果已加载到编辑器、尚未另存或丢弃）
- **打开填充面板 / 发起 fill 或 fill-save 前：**
  - 若 `templateDirty`：先自动 `PUT` 保存模板；失败则中止并提示
  - 若仅 `previewDirty`：**禁止**自动 PUT（避免把已填充结果写成当前模板版本）；允许再次试填（仍基于库中当前版本）、允许「导出」、允许 fill-save；若用户点「保存」则明确提示「将把填充结果保存为新版本，模板占位符会丢失」并需确认
  - 若 URL 带 `?version=`：禁用填充；提示先「保存为当前版本」（追加新 Version）后再填
- 试填成功：前端加载返回的 `workbookJson`，设 `previewDirty=true`、`templateDirty=false`；试填本身不写库
- 用户「放弃试填」：重新 `GET /documents/{id}` 加载当前版本，清除 `previewDirty`

### 5.3 POST /documents/{id}/fill

- Body：填充数据对象（任意 JSON object）
- 响应：

```json
{
  "workbookJson": "{...}",
  "schema": { },
  "warnings": ["缺少字段: Foo"]
}
```

- 响应中的 `schema` 为**填充前**当前版本已存的模板 schema（非填充后重扫）
- 非法 body → 400 `{ "message": "..." }`

### 5.4 POST /documents/{id}/fill-save

- Body：`{ "data": { }, "title": "填充结果-销售订单" }`
- `title` 省略时默认 `{原标题}-填充`
- 基于库中当前版本填充 → 新建 Document → Version 1 = 填充后 workbook + **对填充结果重扫**的 schema
- 响应：`{ "id": "<newDocumentId>", "warnings": [] }`
- 前端：`previewDirty` 不阻碍 fill-save（服务端仍读库中模板）；成功后跳转新文档；历史版本视图下禁用

### 5.5 其它约定

- `POST /documents` 省略 `title` 时默认 `未命名文档`
- Seed 用固定 Document Id（常量 GUID）判断「尚无种子」；标题固定为 `销售订单模板`
- `?version=` 加载走 `GET /documents/{id}/versions/{versionId}`，不走当前版本 GET
- 不支持嵌套 loop；发现则 schema.warnings + 填充跳过
- 循环项内缺字段：与简单字段相同，保留占位符 + warning

**错误约定**

- 404：文档/版本不存在；`{ "message": "文档不存在" }`
- 400：JSON 非法、POST/PUT 缺少 workbookJson；中文 message
- 填充业务告警：一律 200 + `warnings`（含缺字段、跳过的 loop）

## 6. 前端

### 6.1 路由

| 路由 | 页面 |
|------|------|
| `/` | 文档列表：新建、导入 xlsx、进入编辑、查看历史 |
| `/editor/:id` | 编辑器；`?version=` 可选加载历史版本 |
| `/editor/:id/versions` | 版本列表 |

### 6.2 编辑器能力

- Univer Sheets：空白/快照加载、样式编辑
- **导入:** 列表页选文件 → Univer 解析 → `POST /documents` + workbookJson
- **导出:** 工具栏「导出 Excel」→ 当前编辑器 Univer 导出插件下载
- 保存、模板填充面板、返回列表
- 填充面板：展示 schema、编辑 JSON、一键样例、试填预览（加载返回的 workbook）、导出下载、另存为新文档
- 保存失败：sessionStorage 兜底草稿一次

### 6.3 演示数据

- **种子归属:** 后端启动时 Seed 读取仓库内静态文件 `Seed/sales-order-template.workbook.json`（实现时用 Univer 导出一份最小模板提交进库），若尚无种子文档则写入
- 含 `{{CustomerName}}`、`{{OrderDate}}` 与单行 `{{#Items}}`…`{{/Items}}`
- 前端 FillPanel 内置与种子匹配的样例 JSON 按钮

## 7. 后端服务边界

| 单元 | 职责 | 输入 → 输出 |
|------|------|-------------|
| DocumentService | 文档/版本 CRUD、事务内 VersionNo | 命令/查询 → 实体或 DTO |
| TemplateScanner | 扫描 workbook JSON 占位符 | workbookJson → TemplateSchema |
| FillEngine | 简单替换 + 单行循环展开 | workbookJson + data + schema → (workbookJson, warnings) |
| DocumentsController | HTTP 适配 | HTTP ↔ 上述服务 |
| SeedData | 启动写入演示模板 | DbContext |

不设 ExcelBridge。

## 8. 错误处理与边界情况

| 场景 | 行为 |
|------|------|
| 前端导入非 Excel | 前端拦截提示；不调用 API |
| POST/PUT 无 workbookJson | 400 |
| 空循环数组 / 缺少数组 | 删除模板行 |
| 循环标记不匹配 / 多行 loop / 合并冲突 | 保存写入 schema.warnings；填充跳过该 loop + 响应 warnings |
| 缺简单字段 | 保留占位符 + warnings |
| 并发保存 | 最后写入获胜；VersionNo 事务内 max+1 |

## 9. 测试要点

- 空白新建 → 编辑样式 → 保存 → 刷新再打开样式仍在
- 导入带样式 xlsx → 前端导出 → Excel 中关键样式仍在
- 保存 3 次 → 版本列表 3 条 → 打开 v1 再保存 → 出现 v4
- 种子模板 + 样例 JSON → 循环行数正确、简单字段已替换 → Univer 导出可打开
- 缺字段 / 坏 loop 标记 → 200 + warnings

## 10. 实现顺序（供计划引用）

1. 后端骨架 + EF 模型 + Documents/Versions API + Seed
2. 前端列表 + Univer 挂载 + 保存/加载
3. 前端导入 + 前端导出（WYSIWYG）
4. 版本历史页
5. TemplateScanner + FillEngine + 填充 API/面板
6. README 启动说明
