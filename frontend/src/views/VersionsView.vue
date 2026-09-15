<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { listVersions } from '../api/documents'
import type { DocumentVersionSummary } from '../types/document'

const route = useRoute()
const router = useRouter()
const documentId = computed(() => String(route.params.id ?? ''))

const versions = ref<DocumentVersionSummary[]>([])
const loading = ref(false)
const error = ref('')

function formatCreatedAt(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN')
}

async function loadVersions() {
  const id = documentId.value
  if (!id) {
    error.value = '缺少文档 ID'
    return
  }

  loading.value = true
  error.value = ''
  try {
    const { data } = await listVersions(id)
    versions.value = data
  } catch (e) {
    error.value = e instanceof Error ? e.message : '加载版本列表失败'
  } finally {
    loading.value = false
  }
}

function onBack() {
  void router.push(`/editor/${documentId.value}`)
}

function onOpenVersion(version: DocumentVersionSummary) {
  void router.push({
    path: `/editor/${documentId.value}`,
    query: { version: version.id },
  })
}

watch(
  documentId,
  () => {
    void loadVersions()
  },
  { immediate: true },
)
</script>

<template>
  <main class="page">
    <header class="header">
      <h1>版本历史</h1>
      <button type="button" class="btn" @click="onBack">返回编辑</button>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">加载中…</p>
    <p v-else-if="versions.length === 0" class="muted">暂无版本记录。</p>

    <table v-else class="doc-table">
      <thead>
        <tr>
          <th>版本号</th>
          <th>备注</th>
          <th>创建时间</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="ver in versions"
          :key="ver.id"
          class="clickable"
          @click="onOpenVersion(ver)"
        >
          <td>v{{ ver.versionNo }}</td>
          <td>{{ ver.remark || '—' }}</td>
          <td>{{ formatCreatedAt(ver.createdAt) }}</td>
        </tr>
      </tbody>
    </table>
  </main>
</template>

<style scoped>
.page {
  max-width: 960px;
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

.clickable {
  cursor: pointer;
}

.clickable:hover {
  background: #f5f8ff;
}
</style>
