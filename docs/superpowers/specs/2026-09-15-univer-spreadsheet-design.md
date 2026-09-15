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

- 导入含字体/边框/填充色/合并单元格的 xlsx 后，编辑器内样式可见，导出后 Excel 中仍保留
- 每次保存产生可回溯版本；从历史版本打开再保存会追加新版本
- 使用 `{{Field}}` / `{{#List}}` 约定可完成试填并下载填充后的 xlsx
- 无登录；本地启动前后端即可完整体验

## 2. 约束与明确不做

| 约束 | 选择 |
|------|------|
| 前端 | Vue 3 + TypeScript + Vite |
| 后端 | ASP.NET Core (.NET 8) Web API |
| 数据库 | SQLite + EF Core |
| 鉴权 | 无（单机/内网共用文档列表） |
| 占位符 | 单元格文本约定（非侧栏绑定元数据） |

**本次不做:** 登录/多租户、实时协作、公式引擎深度定制、版本差量压缩、对象存储。

## 3. 架构

```
Vue 3 (Univer Sheets)  --REST/JSON-->  ASP.NET Core Web API  --EF Core-->  SQLite
```

- **前端:** 文档列表、Univer 编辑器、版本历史、填充面板；负责 workbook 交互与调用 API
- **后端:** 文档/版本持久化、导入元数据、TemplateSchema 扫描、填充引擎、xlsx 导出缓存
- **真相源:** 每次保存的 `WorkbookJson`（Univer workbook 序列化）；`XlsxBlob` 为导出缓存

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
      Services/         # DocumentService, TemplateScanner, FillEngine, ExcelBridge
      Data/             # DbContext, Entities
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
| XlsxBlob | byte[]? | 保存时生成的 xlsx 缓存 |
| TemplateSchemaJson | string? | 扫描得到的模板 schema |
| CreatedAt | DateTimeOffset | 保存时间 |

**版本行为**

- `PUT` 保存 → 插入新 Version（VersionNo = max+1），更新 Document.CurrentVersionId / UpdatedAt
- 打开历史版本编辑 → 加载该 Version 的 WorkbookJson；再保存仍追加新 Version（不覆盖历史）
- 导出优先用 XlsxBlob；若空则由 workbook 即时导出

### 4.3 TemplateSchema（存于 TemplateSchemaJson）

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
  ]
}
```

### 4.4 占位符约定

| 类型 | 语法 | 示例 |
|------|------|------|
| 简单字段 | `{{FieldName}}` | `{{CustomerName}}` |
| 循环开始 | `{{#ListName}}` | `{{#Items}}`（可单独占一格或与同行字段共存于模板行） |
| 循环结束 | `{{/ListName}}` | `{{/Items}}` |
| 循环内字段 | `{{ListName.Field}}` | `{{Items.Name}}` |

**循环行规则**

- 模板行 = `startRow`..`endRow`（通常单行）；该行样式为蓝本
- 填充时按数组长度复制行：N 条数据 → 保留/生成 N 行，删除标记占位符
- 数组为空 → 删除模板行（或留一行空白，实现选「删除模板行」）
- 简单字段在全表替换；未提供的字段保留原占位符并记入 `warnings`

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

填充结果可不落库（试填下载），或通过 fill-save 另存为新 Document。

## 5. API

基础路径: `/api`

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/documents` | 列表：id, title, updatedAt, currentVersionNo |
| POST | `/documents` | 新建空白文档；body 可选 `{ "title": "..." }` |
| GET | `/documents/{id}` | 详情 + 当前版本 workbookJson + schema |
| PUT | `/documents/{id}` | 保存：`{ workbookJson, title?, remark? }` → 新 Version |
| POST | `/documents/import` | multipart `.xlsx` → 新建 Document+Version |
| GET | `/documents/{id}/versions` | 版本列表（不含大字段） |
| GET | `/documents/{id}/versions/{versionId}` | 某版本 workbookJson |
| GET | `/documents/{id}/export` | 下载当前版本 xlsx |
| GET | `/documents/{id}/schema` | 当前 TemplateSchema |
| POST | `/documents/{id}/fill?format=xlsx\|json` | body = 填充 JSON；返回文件或 workbook |
| POST | `/documents/{id}/fill-save` | 填充并另存为新文档；返回新文档 id |

**错误约定**

- 404：文档/版本不存在
- 400：非 xlsx、损坏文件、JSON 非法；中文 `message`
- 填充缺字段：HTTP 200，body 含 `warnings: string[]`

## 6. 前端

### 6.1 路由

| 路由 | 页面 |
|------|------|
| `/` | 文档列表：新建、导入、进入编辑、查看历史 |
| `/editor/:id` | 编辑器；query `version` 可选，指定历史版本 |
| `/editor/:id/versions` | 版本列表；点击进入 `/editor/:id?version=:vid` |

### 6.2 编辑器能力

- Univer Sheets 空白启动或加载 WorkbookJson
- 官方 Excel 导入/导出插件（样式保留）
- 编辑单元格内容与样式（字体、对齐、边框、填充等）
- 工具栏：保存、导出 Excel、模板填充、返回列表
- 填充面板：展示 schema、编辑/粘贴 JSON、试填预览、下载 xlsx、另存为新文档
- 保存失败时 sessionStorage 兜底本地草稿一次

### 6.3 演示数据

内置种子文档「销售订单模板」：

- 表头区简单字段：`{{CustomerName}}`、`{{OrderDate}}`
- 明细循环行：`{{#Items}}` + `{{Items.Name}}` / `{{Items.Qty}}` / `{{Items.Amount}}` + `{{/Items}}`
- 前端或 API 提供一键试填样例 JSON

## 7. 后端服务边界

| 单元 | 职责 | 依赖 |
|------|------|------|
| DocumentService | CRUD、版本追加、列表 | DbContext |
| ExcelBridge | xlsx ↔ 中间表示 / 与前端约定的导入协助；生成 XlsxBlob | 文件库（如 ClosedXML 兜底） |
| TemplateScanner | 从 WorkbookJson 扫描占位符 → TemplateSchema | 无 |
| FillEngine | 按 schema + 数据改写 workbook（简单替换 + 插行） | TemplateScanner 产出 |
| DocumentsController | HTTP 适配 | 上述 Services |

**填充执行位置:** 服务端在 WorkbookJson 上完成替换与插行，再序列化返回或生成 xlsx。前端 Univer 仅负责展示填充结果（format=json）或触发下载（format=xlsx）。

**导入路径:** 优先前端 Univer 导入插件解析 xlsx 为 workbook，再 `POST /documents` 或专用 import 提交 JSON；若走服务端 multipart import，则后端解析 xlsx 为可被 Univer 加载的 workbook JSON（若服务端无法完美还原 Univer 模型，则强制「前端解析 + 后端只存 JSON」为主路径，multipart 作可选增强）。

**主路径裁定:** **导入以前端 Univer 解析为主**，后端接收 `workbookJson` 建档；导出可由前端插件下载或后端返回 `XlsxBlob`。服务端 FillEngine 只操作 JSON。

## 8. 错误处理与边界情况

| 场景 | 行为 |
|------|------|
| 损坏/非 Excel 上传 | 400，提示「无法解析 Excel 文件」 |
| 空循环数组 | 删除模板行 |
| 循环标记不匹配 | 保存时 schema.warnings；填充时 400 或带 warnings 跳过该 loop |
| 超大 workbook | 不设硬限；SQLite 单库，版本只追加（后续可加保留策略，本次不做） |
| 并发保存 | 最后写入获胜；VersionNo 用事务内 max+1 |

## 9. 测试要点

- 空白新建 → 编辑样式 → 保存 → 刷新再打开样式仍在
- 导入带样式 xlsx → 导出对比关键样式
- 保存 3 次 → 版本列表 3 条 → 打开 v1 再保存 → 出现 v4
- 种子模板 + 样例 JSON → 循环行数量正确、简单字段已替换 → 导出可打开
- 缺字段填充 → 200 + warnings

## 10. 实现顺序（供计划引用）

1. 后端骨架 + EF 模型 + Documents/Versions API
2. 前端列表 + Univer 编辑器挂载 + 保存/加载
3. 导入（前端解析）+ 导出（WYSIWYG）
4. 版本历史页
5. TemplateScanner + FillEngine + 填充 API/面板
6. 种子演示数据 + README 启动说明
