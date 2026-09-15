<script setup lang="ts">
import { ref, watch } from 'vue'
import { getSchema } from '../api/documents'
import type { TemplateSchema } from '../types/document'

const SAMPLE_FILL_JSON = `{
  "CustomerName": "张三公司",
  "OrderDate": "2026-09-15",
  "Items": [
    { "Name": "零件A", "Qty": 2, "Amount": 100 },
    { "Name": "零件B", "Qty": 5, "Amount": 250 }
  ]
}`

const props = defineProps<{
  documentId: string
  open: boolean
  schema?: TemplateSchema | null
  busy?: boolean
}>()

const emit = defineEmits<{
  close: []
  tryFill: [data: object]
  discard: []
  fillSave: [payload: { data: object; title?: string }]
  export: []
}>()

const schemaJson = ref('')
const dataJson = ref(SAMPLE_FILL_JSON)
const localError = ref('')
const loadingSchema = ref(false)
const saveTitle = ref('')

watch(
  () => props.open,
  async (isOpen) => {
    if (!isOpen) return
    localError.value = ''
    if (props.schema) {
      schemaJson.value = JSON.stringify(props.schema, null, 2)
      return
    }
    await fetchSchema()
  },
)

watch(
  () => props.schema,
  (s) => {
    if (s && props.open) {
      schemaJson.value = JSON.stringify(s, null, 2)
    }
  },
)

async function fetchSchema() {
  if (!props.documentId) return
  loadingSchema.value = true
  localError.value = ''
  try {
    const { data } = await getSchema(props.documentId)
    schemaJson.value = JSON.stringify(data, null, 2)
  } catch (e) {
    localError.value = e instanceof Error ? e.message : '加载 schema 失败'
  } finally {
    loadingSchema.value = false
  }
}

function loadSample() {
  dataJson.value = SAMPLE_FILL_JSON
  localError.value = ''
}

function parseData(): object | null {
  try {
    const parsed: unknown = JSON.parse(dataJson.value)
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
      localError.value = '填充数据必须是 JSON 对象'
      return null
    }
    localError.value = ''
    return parsed as object
  } catch {
    localError.value = 'JSON 格式无效'
    return null
  }
}

function onTryFill() {
  const data = parseData()
  if (!data) return
  emit('tryFill', data)
}

function onFillSave() {
  const data = parseData()
  if (!data) return
  const title = saveTitle.value.trim()
  emit('fillSave', title ? { data, title } : { data })
}

function onClose() {
  emit('close')
}
</script>

<template>
  <div v-if="open" class="overlay" @click.self="onClose">
    <aside class="panel" role="dialog" aria-label="模板填充">
      <header class="panel-header">
        <h2>模板填充</h2>
        <button type="button" class="btn" :disabled="busy" @click="onClose">关闭</button>
      </header>

      <p v-if="localError" class="msg error">{{ localError }}</p>
      <p v-if="loadingSchema" class="msg muted">加载 schema…</p>

      <section class="section">
        <h3>Schema</h3>
        <pre class="schema">{{ schemaJson || '（暂无）' }}</pre>
      </section>

      <section class="section">
        <div class="row">
          <h3>填充数据 (JSON)</h3>
          <button type="button" class="btn" :disabled="busy" @click="loadSample">填入样例</button>
        </div>
        <textarea
          v-model="dataJson"
          class="data-input"
          rows="12"
          spellcheck="false"
          :disabled="busy"
          aria-label="填充数据 JSON"
        />
      </section>

      <section class="section">
        <label class="title-label">
          另存标题（可选）
          <input v-model="saveTitle" type="text" class="title-input" :disabled="busy" placeholder="默认：原标题-填充" />
        </label>
      </section>

      <footer class="actions">
        <button type="button" class="btn primary" :disabled="busy" @click="onTryFill">试填</button>
        <button type="button" class="btn" :disabled="busy" @click="emit('discard')">放弃试填</button>
        <button type="button" class="btn" :disabled="busy" @click="onFillSave">另存为新文档</button>
        <button type="button" class="btn" :disabled="busy" @click="emit('export')">导出当前编辑器</button>
      </footer>
    </aside>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 40;
  background: rgba(0, 0, 0, 0.35);
  display: flex;
  justify-content: flex-end;
}

.panel {
  width: min(28rem, 100%);
  height: 100%;
  background: #fff;
  box-shadow: -4px 0 16px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  overflow: auto;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.panel-header h2 {
  margin: 0;
  font-size: 1.1rem;
}

.section h3 {
  margin: 0 0 0.35rem;
  font-size: 0.9rem;
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
}

.row h3 {
  margin: 0;
}

.schema {
  margin: 0;
  padding: 0.5rem;
  max-height: 10rem;
  overflow: auto;
  background: #f6f6f6;
  border: 1px solid #e5e5e5;
  border-radius: 4px;
  font-size: 0.75rem;
  white-space: pre-wrap;
  word-break: break-word;
}

.data-input {
  width: 100%;
  box-sizing: border-box;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 0.8rem;
  padding: 0.5rem;
  border: 1px solid #ccc;
  border-radius: 4px;
  resize: vertical;
}

.title-label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.875rem;
}

.title-input {
  padding: 0.35rem 0.5rem;
  border: 1px solid #ccc;
  border-radius: 4px;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: auto;
  padding-top: 0.5rem;
}

.msg {
  margin: 0;
  font-size: 0.875rem;
}

.msg.error {
  color: #b00020;
}

.msg.muted {
  color: #666;
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
</style>
