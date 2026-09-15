export interface TemplateLoop {
  name: string
  sheet: string
  startRow: number
  endRow: number
  fields: string[]
}

export interface TemplateSchema {
  fields: string[]
  loops: TemplateLoop[]
  warnings: string[]
}

export interface DocumentListItem {
  id: string
  title: string
  updatedAt: string
  currentVersionNo: number
}

export interface DocumentDetail {
  id: string
  title: string
  currentVersionId: string
  versionNo: number
  workbookJson: string
  schema?: TemplateSchema | null
}

export interface DocumentVersionSummary {
  id: string
  versionNo: number
  remark?: string | null
  createdAt: string
}

export interface DocumentVersionDetail {
  id: string
  versionNo: number
  workbookJson: string
  schema?: TemplateSchema | null
  createdAt: string
  remark?: string | null
}

export interface CreateDocumentRequest {
  title?: string | null
  workbookJson: string
  remark?: string | null
}

export interface UpdateDocumentRequest {
  workbookJson: string
  title?: string | null
  remark?: string | null
}

export interface FillResponse {
  workbookJson: string
  schema?: TemplateSchema | null
  warnings: string[]
}

export interface FillSaveRequest {
  data: Record<string, unknown>
  title?: string | null
}

export interface FillSaveResponse {
  id: string
  warnings: string[]
}

export interface ErrorResponse {
  message: string
}
