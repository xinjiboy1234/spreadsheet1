<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { create, list } from '../api/documents'
import type { DocumentListItem } from '../types/document'
import { emptyWorkbookJson } from '../utils/emptyWorkbook'
import { importExcelFile } from '../utils/excelIo'

const router = useRouter()
const documents = ref<DocumentListItem[]>([])
const loading = ref(false)
const creating = ref(false)
const importing = ref(false)
const error = ref('')
const fileInput = ref<HTMLInputElement | null>(null)

async function loadList() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await list()
    documents.value = data
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载文档列表失败'
  } finally {
    loading.value = false
  }
}

async function onCreate() {
  if (creating.value) return
  creating.value = true
  error.value = ''
  try {
    const { data } = await create({
      title: '未命名文档',
      workbookJson: emptyWorkbookJson(),
    })
    await router.push(`/editor/${data.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '新建文档失败'
    creating.value = false
  }
}

function onImportClick() {
  if (importing.value) return
  fileInput.value?.click()
}

function isExcelFile(file: File) {
  const name = file.name.toLowerCase()
  return name.endsWith('.xlsx') || name.endsWith('.xls')
}

function titleFromFileName(fileName: string) {
  return fileName.replace(/\.(xlsx|xls)$/i, '') || '未命名文档'
}

async function onImportFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  if (!isExcelFile(file)) {
    alert('请选择 Excel 文件')
    return
  }

  importing.value = true
  error.value = ''
  try {
    const snapshot = await importExcelFile(file)
    const { data } = await create({
      title: titleFromFileName(file.name),
      workbookJson: JSON.stringify(snapshot),
    })
    await router.push(`/editor/${data.id}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '导入失败'
    importing.value = false
  }
}

function formatUpdatedAt(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN')
}

onMounted(() => {
  void loadList()
})
</script>

<template>
  <main class="page">
    <header class="header">
      <h1>文档列表</h1>
      <div class="actions">
        <RouterLink class="btn" to="/rich-text">富文本编辑器</RouterLink>
        <button type="button" class="btn primary" :disabled="creating" @click="onCreate">
          {{ creating ? '新建中…' : '新建' }}
        </button>
        <button type="button" class="btn" :disabled="importing" @click="onImportClick">
          {{ importing ? '导入中…' : '导入' }}
        </button>
        <input
          ref="fileInput"
          type="file"
          accept=".xlsx,.xls,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-excel"
          class="file-input"
          @change="onImportFileChange"
        />
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">加载中…</p>
    <p v-else-if="documents.length === 0" class="muted">暂无文档，点击「新建」开始。</p>

    <table v-else class="doc-table">
      <thead>
        <tr>
          <th>标题</th>
          <th>更新时间</th>
          <th>版本</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="doc in documents" :key="doc.id">
          <td>{{ doc.title || '未命名文档' }}</td>
          <td>{{ formatUpdatedAt(doc.updatedAt) }}</td>
          <td>v{{ doc.currentVersionNo }}</td>
          <td class="row-actions">
            <RouterLink :to="`/editor/${doc.id}`">打开</RouterLink>
            <RouterLink :to="`/editor/${doc.id}/versions`">历史</RouterLink>
          </td>
        </tr>
      </tbody>
    </table>
  </main>
</template>

<style scoped>
.page {
  box-sizing: border-box;
  width: 100%;
  max-width: 960px;
  min-height: 100%;
  margin: 0 auto;
  padding: 1.5rem;
}

.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.header h1 {
  margin: 0;
}

.actions {
  display: flex;
  gap: 0.5rem;
}

.file-input {
  display: none;
}

.btn {
  display: inline-flex;
  align-items: center;
  padding: 0.4rem 0.85rem;
  border: 1px solid #ccc;
  border-radius: 4px;
  background: #fff;
  color: inherit;
  cursor: pointer;
  font-size: 0.875rem;
  text-decoration: none;
}

.btn:hover:not(:disabled) {
  background: #f5f5f5;
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

.muted {
  color: #666;
}

.error {
  color: #b00020;
}

.doc-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.doc-table th,
.doc-table td {
  padding: 0.65rem 0.5rem;
  border-bottom: 1px solid #e5e5e5;
  text-align: left;
}

.doc-table th {
  color: #555;
  font-weight: 600;
}

.row-actions {
  display: flex;
  gap: 0.75rem;
}

.row-actions a {
  color: #2f6fed;
  text-decoration: none;
}

.row-actions a:hover {
  text-decoration: underline;
}
</style>
