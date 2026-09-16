<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import TiptapEditor from '../components/TiptapEditor.vue'

const STORAGE_KEY = 'spreadsheet-prototype:tiptap-draft'

const router = useRouter()
const content = ref('')
const title = ref('未命名富文本文档')
const savedAt = ref('')
const dirty = ref(false)
const editorRef = ref<InstanceType<typeof TiptapEditor> | null>(null)

onMounted(() => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return
    const parsed = JSON.parse(raw) as { title?: string; content?: string; savedAt?: string }
    if (parsed.title) title.value = parsed.title
    if (parsed.content) content.value = parsed.content
    if (parsed.savedAt) savedAt.value = parsed.savedAt
  } catch {
    // ignore corrupt local draft
  }
})

watch(content, () => {
  dirty.value = true
})

watch(title, () => {
  dirty.value = true
})

function saveLocal() {
  const html = editorRef.value?.getHTML?.() ?? content.value
  content.value = html
  const now = new Date().toLocaleString('zh-CN')
  localStorage.setItem(
    STORAGE_KEY,
    JSON.stringify({
      title: title.value,
      content: html,
      savedAt: now,
    }),
  )
  savedAt.value = now
  dirty.value = false
}

function exportHtml() {
  const html = editorRef.value?.getHTML?.() ?? content.value
  const blob = new Blob(
    [
      `<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>${escapeHtml(title.value)}</title></head><body>${html}</body></html>`,
    ],
    { type: 'text/html;charset=utf-8' },
  )
  downloadBlob(blob, `${safeFileName(title.value)}.html`)
}

function clearDraft() {
  if (!window.confirm('清空当前草稿？此操作不可恢复。')) return
  content.value = '<p></p>'
  title.value = '未命名富文本文档'
  localStorage.removeItem(STORAGE_KEY)
  savedAt.value = ''
  dirty.value = false
  editorRef.value?.setContent?.('<p></p>')
}

function escapeHtml(value: string) {
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

function goBack() {
  void router.push('/')
}
</script>

<template>
  <main class="page">
    <header class="header">
      <div class="left">
        <button type="button" class="btn" @click="goBack">返回</button>
        <input v-model="title" class="title-input" type="text" maxlength="120" aria-label="文档标题" />
        <span class="meta">
          <template v-if="dirty">未保存（仅本地）</template>
          <template v-else-if="savedAt">已保存 {{ savedAt }}</template>
          <template v-else>前端演示 · 无后端</template>
        </span>
      </div>
      <div class="actions">
        <button type="button" class="btn" @click="clearDraft">清空</button>
        <button type="button" class="btn" @click="exportHtml">导出 HTML</button>
        <button type="button" class="btn primary" @click="saveLocal">保存到本地</button>
      </div>
    </header>

    <section class="editor-wrap">
      <TiptapEditor ref="editorRef" v-model="content" />
    </section>
  </main>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
  width: 100%;
  max-width: 1100px;
  height: 100%;
  min-height: 0;
  margin: 0 auto;
  padding: 1rem 1.25rem 1.25rem;
}

.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.85rem;
  flex-wrap: wrap;
}

.left {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  min-width: 0;
  flex: 1 1 auto;
}

.title-input {
  min-width: 0;
  flex: 1 1 220px;
  max-width: 420px;
  height: 34px;
  padding: 0 0.65rem;
  border: 1px solid #d0d5e0;
  border-radius: 4px;
  font-size: 1rem;
  font-weight: 600;
}

.meta {
  color: #6b7385;
  font-size: 0.8rem;
  white-space: nowrap;
}

.actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.btn {
  padding: 0.4rem 0.85rem;
  border: 1px solid #ccc;
  border-radius: 4px;
  background: #fff;
  cursor: pointer;
  font-size: 0.875rem;
}

.btn:hover {
  background: #f5f5f5;
}

.btn.primary {
  border-color: #2f6fed;
  background: #2f6fed;
  color: #fff;
}

.btn.primary:hover {
  background: #2558c7;
}

.editor-wrap {
  flex: 1 1 auto;
  min-height: 0;
}
</style>
