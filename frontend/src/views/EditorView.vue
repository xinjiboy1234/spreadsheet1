<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { fill, fillSave, get, getVersion, save } from '../api/documents'
import FillPanel from '../components/FillPanel.vue'
import UniverSheetHost from '../components/UniverSheetHost.vue'
import { useEditorDirty } from '../composables/useEditorDirty'
import type { TemplateSchema } from '../types/document'
import { emptyWorkbookJson } from '../utils/emptyWorkbook'
import { exportExcelFile } from '../utils/excelIo'

type SheetHostExpose = {
  getWorkbookJson: () => string
  loadWorkbookJson: (json: string, gridlinesVisible?: boolean) => void
  setGridlinesVisible: (visible: boolean) => void
  getRangeHtml: () => { html: string; rangeA1: string }
}

const route = useRoute()
const router = useRouter()
const documentId = computed(() => String(route.params.id ?? ''))
const versionQueryId = computed(() => {
  const v = route.query.version
  return typeof v === 'string' && v !== '' ? v : ''
})
const viewingHistory = computed(() => versionQueryId.value !== '')

const {
  templateDirty,
  previewDirty,
  markTemplateEdit,
  markPreview,
  clearPreview,
  clearAll,
  resetAfterSave,
} = useEditorDirty()

const sheetHost = ref<SheetHostExpose | null>(null)
const title = ref('')
const versionNo = ref<number | null>(null)
const workbookJson = ref<string | null>(null)
const ready = ref(false)
const loading = ref(false)
const saving = ref(false)
const fillBusy = ref(false)
const fillPanelOpen = ref(false)
const showGridlines = ref(false)
const error = ref('')
const status = ref('')
const warnings = ref<string[]>([])
const cachedSchema = ref<TemplateSchema | null>(null)

function onToggleGridlines() {
  sheetHost.value?.setGridlinesVisible(showGridlines.value)
}

function draftKey(id: string) {
  return `draft:${id}`
}

function clearDraft(id: string) {
  sessionStorage.removeItem(draftKey(id))
}

function writeDraft(id: string, json: string) {
  sessionStorage.setItem(draftKey(id), json)
}

function readDraft(id: string): string | null {
  return sessionStorage.getItem(draftKey(id))
}

function showWarnings(list: string[] | undefined | null) {
  warnings.value = list?.length ? [...list] : []
}

async function loadDocument() {
  const id = documentId.value
  if (!id) {
    error.value = '缺少文档 ID'
    return
  }

  clearAll()
  loading.value = true
  error.value = ''
  status.value = ''
  warnings.value = []
  ready.value = false
  workbookJson.value = null
  fillPanelOpen.value = false
  showGridlines.value = false
  cachedSchema.value = null

  try {
    const historyVersionId = versionQueryId.value
    if (historyVersionId) {
      const [{ data: doc }, { data: ver }] = await Promise.all([
        get(id),
        getVersion(id, historyVersionId),
      ])
      title.value = doc.title || '未命名文档'
      versionNo.value = ver.versionNo
      workbookJson.value = ver.workbookJson || emptyWorkbookJson()
      cachedSchema.value = ver.schema ?? null
      ready.value = true
      return
    }

    const { data } = await get(id)
    title.value = data.title || '未命名文档'
    versionNo.value = data.versionNo
    cachedSchema.value = data.schema ?? null

    const draft = readDraft(id)
    if (draft != null && draft !== '') {
      const restore = window.confirm('检测到未保存的本地草稿，是否恢复？')
      if (restore) {
        workbookJson.value = draft
        ready.value = true
        markTemplateEdit()
        status.value = '已恢复本地草稿'
        return
      }
      clearDraft(id)
    }

    workbookJson.value = data.workbookJson || emptyWorkbookJson()
    ready.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载文档失败'
  } finally {
    loading.value = false
  }
}

function onSheetChange() {
  markTemplateEdit()
  status.value = ''
}

/** Persist current editor as template when needed before fill/fill-save. */
async function ensureTemplateSavedForFill(): Promise<boolean> {
  if (previewDirty.value) {
    return true
  }
  if (!templateDirty.value) {
    return true
  }
  const id = documentId.value
  if (!id || !sheetHost.value) {
    error.value = '无法保存模板'
    return false
  }

  saving.value = true
  error.value = ''
  status.value = ''
  const json = sheetHost.value.getWorkbookJson()
  try {
    const { data } = await save(id, {
      workbookJson: json,
      title: title.value || undefined,
    })
    title.value = data.title || title.value
    versionNo.value = data.versionNo
    cachedSchema.value = data.schema ?? null
    clearDraft(id)
    resetAfterSave()
    status.value = '模板已保存'
    return true
  } catch {
    writeDraft(id, json)
    error.value = '保存模板失败，已写入本地草稿；已中止填充'
    return false
  } finally {
    saving.value = false
  }
}

async function onSave() {
  const id = documentId.value
  if (!id || !sheetHost.value || saving.value) return

  if (previewDirty.value) {
    const ok = window.confirm('将把填充结果保存为新版本，模板占位符会丢失')
    if (!ok) return
  }

  saving.value = true
  error.value = ''
  status.value = ''

  const json = sheetHost.value.getWorkbookJson()
  try {
    const { data } = await save(id, {
      workbookJson: json,
      title: title.value || undefined,
    })
    title.value = data.title || title.value
    versionNo.value = data.versionNo
    cachedSchema.value = data.schema ?? null
    clearDraft(id)
    clearAll()
    status.value = '保存成功'
    if (viewingHistory.value) {
      await router.replace({ path: `/editor/${id}` })
    }
  } catch (e) {
    writeDraft(id, json)
    error.value = '保存失败，已写入本地草稿'
  } finally {
    saving.value = false
  }
}

function onTitleInput() {
  markTemplateEdit()
  status.value = ''
}

function onBack() {
  void router.push('/')
}

function onHistory() {
  void router.push(`/editor/${documentId.value}/versions`)
}

async function onExportExcel() {
  if (!sheetHost.value || !ready.value) return
  error.value = ''
  status.value = ''
  try {
    const snapshot = JSON.parse(sheetHost.value.getWorkbookJson()) as object
    const base = (title.value || '未命名文档').trim() || '未命名文档'
    const fileName = base.toLowerCase().endsWith('.xlsx') ? base : `${base}.xlsx`
    await exportExcelFile(snapshot, fileName)
    status.value = '导出成功'
  } catch (e) {
    error.value = e instanceof Error ? e.message : '导出失败'
  }
}

function escapeHtmlAttr(value: string) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}

function safeFileName(value: string) {
  return value.replace(/[\\/:*?"<>|]+/g, '_').trim() || 'document'
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}

async function onGetHtml() {
  if (!sheetHost.value || !ready.value) return
  error.value = ''
  status.value = ''
  try {
    const { html, rangeA1 } = sheetHost.value.getRangeHtml()
    const docTitle = (title.value || '未命名文档').trim() || '未命名文档'
    const fullDoc = `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>${escapeHtmlAttr(docTitle)} · ${escapeHtmlAttr(rangeA1)}</title>
<style>
  body { margin: 16px; background: #fff; }
</style>
</head>
<body>
${html}
</body>
</html>`

    downloadBlob(
      new Blob([fullDoc], { type: 'text/html;charset=utf-8' }),
      `${safeFileName(docTitle)}_${rangeA1.replaceAll(':', '-')}.html`,
    )

    try {
      await navigator.clipboard.write([
        new ClipboardItem({
          'text/html': new Blob([html], { type: 'text/html' }),
          'text/plain': new Blob([html], { type: 'text/plain' }),
        }),
      ])
      status.value = `已导出并复制 HTML（${rangeA1}，保真剪贴板样式）`
    } catch {
      status.value = `已导出 HTML（${rangeA1}）；剪贴板复制失败，可直接打开下载文件`
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : '获取 HTML 失败'
  }
}

async function openFillPanel() {
  if (viewingHistory.value || !ready.value) return
  const saved = await ensureTemplateSavedForFill()
  if (!saved) return
  fillPanelOpen.value = true
}

async function onTryFill(data: object) {
  const id = documentId.value
  if (!id || !sheetHost.value || fillBusy.value) return

  fillBusy.value = true
  error.value = ''
  status.value = ''
  try {
    const saved = await ensureTemplateSavedForFill()
    if (!saved) return

    const { data: result } = await fill(id, data)
    sheetHost.value.loadWorkbookJson(result.workbookJson, showGridlines.value)
    markPreview()
    if (result.schema) {
      cachedSchema.value = result.schema
    }
    showWarnings(result.warnings)
    status.value = '试填成功'
  } catch (e) {
    error.value = e instanceof Error ? e.message : '试填失败'
  } finally {
    fillBusy.value = false
  }
}

async function onDiscardPreview() {
  const id = documentId.value
  if (!id || !sheetHost.value || fillBusy.value) return

  fillBusy.value = true
  error.value = ''
  status.value = ''
  warnings.value = []
  try {
    const { data } = await get(id)
    sheetHost.value.loadWorkbookJson(data.workbookJson || emptyWorkbookJson(), showGridlines.value)
    title.value = data.title || title.value
    versionNo.value = data.versionNo
    cachedSchema.value = data.schema ?? null
    clearPreview()
    status.value = '已放弃试填'
  } catch (e) {
    error.value = e instanceof Error ? e.message : '放弃试填失败'
  } finally {
    fillBusy.value = false
  }
}

async function onFillSave(payload: { data: object; title?: string }) {
  const id = documentId.value
  if (!id || fillBusy.value) return

  fillBusy.value = true
  error.value = ''
  status.value = ''
  try {
    const saved = await ensureTemplateSavedForFill()
    if (!saved) return

    const { data: result } = await fillSave(id, {
      data: payload.data as Record<string, unknown>,
      title: payload.title,
    })
    showWarnings(result.warnings)
    fillPanelOpen.value = false
    await router.push(`/editor/${result.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '另存失败'
  } finally {
    fillBusy.value = false
  }
}

watch(
  [documentId, versionQueryId],
  () => {
    void loadDocument()
  },
  { immediate: true },
)
</script>

<template>
  <main class="editor-page">
    <header class="toolbar">
      <div class="toolbar-left">
        <button type="button" class="btn" @click="onBack">返回列表</button>
        <input
          v-model="title"
          class="title-input"
          type="text"
          aria-label="文档标题"
          @input="onTitleInput"
        />
        <span v-if="versionNo != null" class="meta">v{{ versionNo }}</span>
        <span v-if="templateDirty" class="dirty">未保存</span>
        <span v-if="previewDirty" class="dirty preview">试填预览</span>
      </div>
      <div class="toolbar-right">
        <label class="gridlines-toggle" title="显示或隐藏单元格网格线">
          <input
            v-model="showGridlines"
            type="checkbox"
            :disabled="!ready"
            @change="onToggleGridlines"
          />
          <span>网格线</span>
        </label>
        <button type="button" class="btn primary" :disabled="saving || !ready" @click="onSave">
          {{ saving ? '保存中…' : '保存' }}
        </button>
        <button type="button" class="btn" :disabled="!ready" @click="onExportExcel">导出</button>
        <button
          type="button"
          class="btn"
          :disabled="!ready"
          title="按当前选区生成保真 HTML（单格会扩展到数据区域）；同时下载并复制"
          @click="onGetHtml"
        >
          获取 HTML
        </button>
        <button
          type="button"
          class="btn"
          :disabled="viewingHistory || !ready || saving"
          @click="openFillPanel"
        >
          填充
        </button>
        <button type="button" class="btn" @click="onHistory">历史</button>
      </div>
    </header>

    <p v-if="viewingHistory" class="banner warn">正在查看历史版本</p>
    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-else-if="status" class="banner ok">{{ status }}</p>
    <ul v-if="warnings.length" class="banner warn warnings">
      <li v-for="(w, i) in warnings" :key="i">{{ w }}</li>
    </ul>
    <p v-if="loading" class="banner muted">加载中…</p>

    <UniverSheetHost
      v-if="ready && workbookJson != null"
      ref="sheetHost"
      class="sheet"
      :workbook-json="workbookJson"
      @change="onSheetChange"
    />

    <FillPanel
      :document-id="documentId"
      :open="fillPanelOpen"
      :schema="cachedSchema"
      :busy="fillBusy || saving"
      @close="fillPanelOpen = false"
      @try-fill="onTryFill"
      @discard="onDiscardPreview"
      @fill-save="onFillSave"
      @export="onExportExcel"
    />
  </main>
</template>

<style scoped>
.editor-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.toolbar {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.6rem 1rem;
  border-bottom: 1px solid #e5e5e5;
  background: #fafafa;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.title-input {
  min-width: 12rem;
  max-width: 20rem;
  padding: 0.35rem 0.5rem;
  border: 1px solid #ccc;
  border-radius: 4px;
  font-size: 0.95rem;
}

.meta {
  color: #666;
  font-size: 0.875rem;
}

.dirty {
  color: #b45309;
  font-size: 0.875rem;
}

.dirty.preview {
  color: #1d4ed8;
}

.gridlines-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.875rem;
  color: #444;
  user-select: none;
  cursor: pointer;
}

.gridlines-toggle:has(input:disabled) {
  opacity: 0.6;
  cursor: not-allowed;
}

.gridlines-toggle input {
  margin: 0;
  cursor: inherit;
}

.btn {
  padding: 0.35rem 0.75rem;
  border: 1px solid #ccc;
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  font-size: 0.875rem;
}

.btn:hover:not(:disabled) {
  background: #f0f0f0;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn.primary {
  border-color: #2f6fed;
  background: #2f6fed;
  color: #fff;
}

.btn.primary:hover:not(:disabled) {
  background: #2558c7;
}

.banner {
  flex: 0 0 auto;
  margin: 0;
  padding: 0.4rem 1rem;
  font-size: 0.875rem;
}

.banner.error {
  color: #b00020;
  background: #fdecea;
}

.banner.warn {
  color: #92400e;
  background: #fffbeb;
}

.banner.ok {
  color: #1b5e20;
  background: #e8f5e9;
}

.banner.muted {
  color: #666;
}

.warnings {
  list-style: disc;
  padding-left: 1.75rem;
  margin: 0;
}

.sheet {
  flex: 1 1 auto;
  min-height: 0;
}
</style>

