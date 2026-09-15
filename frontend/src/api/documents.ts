import type {
  CreateDocumentRequest,
  DocumentDetail,
  DocumentListItem,
  DocumentVersionDetail,
  DocumentVersionSummary,
  FillResponse,
  FillSaveRequest,
  FillSaveResponse,
  TemplateSchema,
  UpdateDocumentRequest,
} from '../types/document'
import { http } from './http'

export function list() {
  return http.get<DocumentListItem[]>('/documents')
}

export function create(request: CreateDocumentRequest) {
  return http.post<DocumentDetail>('/documents', request)
}

export function get(id: string) {
  return http.get<DocumentDetail>(`/documents/${id}`)
}

export function save(id: string, request: UpdateDocumentRequest) {
  return http.put<DocumentDetail>(`/documents/${id}`, request)
}

export function listVersions(id: string) {
  return http.get<DocumentVersionSummary[]>(`/documents/${id}/versions`)
}

export function getVersion(id: string, versionId: string) {
  return http.get<DocumentVersionDetail>(`/documents/${id}/versions/${versionId}`)
}

export function getSchema(id: string) {
  return http.get<TemplateSchema>(`/documents/${id}/schema`)
}

export function fill(id: string, data: object) {
  return http.post<FillResponse>(`/documents/${id}/fill`, data)
}

export function fillSave(id: string, body: FillSaveRequest) {
  return http.post<FillSaveResponse>(`/documents/${id}/fill-save`, body)
}
