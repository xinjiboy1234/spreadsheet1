import type {
  CreateDocumentRequest,
  DocumentDetail,
  DocumentListItem,
  DocumentVersionDetail,
  DocumentVersionSummary,
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

// fill / schema — Task 11
