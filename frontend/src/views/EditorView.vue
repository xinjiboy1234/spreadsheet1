<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { get, save } from '../api/documents'
import UniverSheetHost from '../components/UniverSheetHost.vue'
import { useEditorDirty } from '../composables/useEditorDirty'
import { emptyWorkbookJson } from '../utils/emptyWorkbook'

type SheetHostExpose = {
  getWorkbookJson: () => string
  loadWorkbookJson: (json: string) => void
}

const route = useRoute()
const router = useRouter()
const documentId = computed(() => String(route.params.id ?? ''))

const { templateDirty, markTemplateEdit, resetAfterSave } = useEditorDirty()

const sheetHost = ref<SheetHostExpose | null>(null)
const title = ref('')
const versionNo = ref<number | null>(null)
const workbookJson = ref<string | null>(null)
const ready = ref(false)
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const status = ref('')

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

async function loadDocument() {
  const id = documentId.value
  if (!id) {
    error.value = '缺少文档 ID'
    return
  }

  loading.value = true
  error.value = ''
  status.value = ''
  ready.value = false
  workbookJson.value = null

  try {
    const { data } = await get(id)
    title.value = data.title || '未命名文档'
    versionNo.value = data.versionNo

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

async function onSave() {
  const id = documentId.value
  if (!id || !sheetHost.value || saving.value) return

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
    clearDraft(id)
    resetAfterSave()
    status.value = '保存成功'
  } catch (e) {
    writeDraft(id, json)
    error.value = e instanceof Error ? e.message : '保存失败'
    status.value = '已写入本地草稿'
  } finally {
    saving.value = false
  }
}

function onBack() {
  void router.push('/')
}

function onHistory() {
  void router.push(`/editor/${documentId.value}/versions`)
}

function onExportPlaceholder() {
  alert('导出功能即将实现')
}

function onFillPlaceholder() {
  alert('填充功能即将实现')
}

onMounted(() => {
  void loadDocument()
})
</script>

<template>
  <main class="editor-page">
    <header class="toolbar">
      <div class="toolbar-left">
        <button type="button" class="btn" @click="onBack">返回列表</button>
        <input v-model="title" class="title-input" type="text" aria-label="文档标题" />
        <span v-if="versionNo != null" class="meta">v{{ versionNo }}</span>
        <span v-if="templateDirty" class="dirty">未保存</span>
      </div>
      <div class="toolbar-right">
        <button type="button" class="btn primary" :disabled="saving || !ready" @click="onSave">
          {{ saving ? '保存中…' : '保存' }}
        </button>
        <button type="button" class="btn" @click="onExportPlaceholder">导出</button>
        <button type="button" class="btn" @click="onFillPlaceholder">填充</button>
        <button type="button" class="btn" @click="onHistory">历史</button>
      </div>
    </header>

    <p v-if="error" class="banner error">{{ error }}</p>
    <p v-else-if="status" class="banner ok">{{ status }}</p>
    <p v-if="loading" class="banner muted">加载中…</p>

    <UniverSheetHost
      v-if="ready && workbookJson != null"
      ref="sheetHost"
      class="sheet"
      :workbook-json="workbookJson"
      @change="onSheetChange"
    />
  </main>
</template>

<style scoped>
.editor-page {
  display: flex;
  flex-direction: column;
  height: 100vh;
  min-height: 0;
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

.banner.ok {
  color: #1b5e20;
  background: #e8f5e9;
}

.banner.muted {
  color: #666;
}

.sheet {
  flex: 1 1 auto;
  min-height: 0;
}
</style>
