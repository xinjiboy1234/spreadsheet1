<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import { TextStyleKit } from '@tiptap/extension-text-style'
import TextAlign from '@tiptap/extension-text-align'
import Highlight from '@tiptap/extension-highlight'
import Image from '@tiptap/extension-image'
import { TableKit } from '@tiptap/extension-table'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import Placeholder from '@tiptap/extension-placeholder'
import CharacterCount from '@tiptap/extension-character-count'
import Typography from '@tiptap/extension-typography'
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight'
import Youtube from '@tiptap/extension-youtube'
import { common, createLowlight } from 'lowlight'
import {
  ResizableTableRow,
  TableRowResize,
  getSelectedColumnWidth,
  getSelectedRowHeight,
  setSelectedColumnWidth,
  setSelectedRowHeight,
} from '../extensions/resizableTable'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    placeholder?: string
  }>(),
  {
    modelValue: '',
    placeholder: '开始输入内容…',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const lowlight = createLowlight(common)

const COLORS = [
  '#000000',
  '#434343',
  '#666666',
  '#999999',
  '#b00020',
  '#e53935',
  '#fb8c00',
  '#fdd835',
  '#43a047',
  '#1e88e5',
  '#8e24aa',
  '#ffffff',
]

const HIGHLIGHTS = [
  '#fef08a',
  '#bbf7d0',
  '#bfdbfe',
  '#fbcfe8',
  '#e9d5ff',
  '#fed7aa',
  '#e5e7eb',
]

const CODE_LANGUAGES = [
  { label: 'Plain Text', value: 'plaintext' },
  { label: 'JavaScript', value: 'javascript' },
  { label: 'TypeScript', value: 'typescript' },
  { label: 'HTML', value: 'xml' },
  { label: 'CSS', value: 'css' },
  { label: 'JSON', value: 'json' },
  { label: 'Python', value: 'python' },
  { label: 'Java', value: 'java' },
  { label: 'C#', value: 'csharp' },
  { label: 'C/C++', value: 'cpp' },
  { label: 'SQL', value: 'sql' },
  { label: 'Bash', value: 'bash' },
  { label: 'Markdown', value: 'markdown' },
  { label: 'YAML', value: 'yaml' },
  { label: 'Go', value: 'go' },
  { label: 'Rust', value: 'rust' },
  { label: 'PHP', value: 'php' },
]

const FONT_SIZES = ['12px', '14px', '16px', '18px', '20px', '24px', '28px', '32px']
const FONT_FAMILIES = [
  { label: '默认', value: '' },
  { label: '宋体', value: 'SimSun, serif' },
  { label: '黑体', value: 'SimHei, sans-serif' },
  { label: '微软雅黑', value: '"Microsoft YaHei", sans-serif' },
  { label: '楷体', value: 'KaiTi, serif' },
  { label: 'Arial', value: 'Arial, sans-serif' },
  { label: 'Georgia', value: 'Georgia, serif' },
  { label: 'Monospace', value: 'ui-monospace, monospace' },
]

const editor = useEditor({
  content: props.modelValue || getDefaultContent(),
  extensions: [
    StarterKit.configure({
      codeBlock: false,
      link: {
        openOnClick: false,
        HTMLAttributes: { rel: 'noopener noreferrer', target: '_blank' },
      },
    }),
    TextStyleKit,
    TextAlign.configure({ types: ['heading', 'paragraph'] }),
    Highlight.configure({ multicolor: true }),
    Image.configure({ allowBase64: true }),
    TableKit.configure({
      table: {
        resizable: true,
        handleWidth: 8,
        cellMinWidth: 40,
        lastColumnResizable: true,
      },
      tableRow: false,
    }),
    ResizableTableRow,
    TableRowResize,
    TaskList,
    TaskItem.configure({ nested: true }),
    Subscript,
    Superscript,
    Placeholder.configure({ placeholder: props.placeholder }),
    CharacterCount,
    Typography,
    CodeBlockLowlight.configure({
      lowlight,
      defaultLanguage: 'plaintext',
      enableTabIndentation: true,
      tabSize: 2,
    }),
    Youtube.configure({ controls: true, nocookie: true }),
  ],
  editorProps: {
    attributes: {
      class: 'tiptap-prose',
    },
  },
  onUpdate: ({ editor: ed }) => {
    emit('update:modelValue', ed.getHTML())
  },
})

watch(
  () => props.modelValue,
  (value) => {
    if (!editor.value) return
    const current = editor.value.getHTML()
    if (value !== undefined && value !== current) {
      editor.value.commands.setContent(value, { emitUpdate: false })
    }
  },
)

const sourceMode = ref(false)
const sourceHtml = ref('')

const charCount = computed(() => editor.value?.storage.characterCount?.characters() ?? 0)
const wordCount = computed(() => editor.value?.storage.characterCount?.words() ?? 0)
const currentCodeLanguage = computed(() => {
  if (!editor.value?.isActive('codeBlock')) return 'plaintext'
  return (editor.value.getAttributes('codeBlock').language as string | null) || 'plaintext'
})

function getDefaultContent() {
  return `
    <h1>富文本编辑器</h1>
    <p>基于 <a href="https://tiptap.dev/docs/editor/getting-started/overview">Tiptap</a> 的前端演示页，后端暂未接入。可试用工具栏全部能力。</p>
    <ul>
      <li>标题、段落、引用、分割线</li>
      <li>粗体 / 斜体 / 下划线 / 删除线 / 高亮 / 颜色</li>
      <li>列表、任务列表、对齐、表格、图片、链接、代码块、YouTube</li>
      <li>工具栏「源码」可切换 HTML 源码编辑；代码块支持语法高亮与语言切换</li>
    </ul>
    <pre><code class="language-javascript">function hello(name) {
  console.log(\`Hello, \${name}!\`)
}

hello('Tiptap')</code></pre>
    <p>表格可拖拽列边界调列宽，拖拽行底边调行高；也可用工具栏「列宽」「行高」输入数值。</p>
    <table>
      <tbody>
        <tr>
          <th><p>列 A</p></th>
          <th><p>列 B</p></th>
          <th><p>列 C</p></th>
        </tr>
        <tr>
          <td><p>拖右边线改列宽</p></td>
          <td><p>拖底边线改行高</p></td>
          <td><p>示例</p></td>
        </tr>
        <tr>
          <td><p>单元格</p></td>
          <td><p>单元格</p></td>
          <td><p>单元格</p></td>
        </tr>
      </tbody>
    </table>
  `
}

function run(fn: (ed: NonNullable<typeof editor.value>) => void) {
  if (!editor.value) return
  fn(editor.value)
}

function setLink() {
  run((ed) => {
    const prev = ed.getAttributes('link').href as string | undefined
    const url = window.prompt('链接地址', prev || 'https://')
    if (url === null) return
    if (url === '') {
      ed.chain().focus().extendMarkRange('link').unsetLink().run()
      return
    }
    ed.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
  })
}

function addImageByUrl() {
  const url = window.prompt('图片 URL', 'https://')
  if (!url) return
  run((ed) => ed.chain().focus().setImage({ src: url }).run())
}

function onImageFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file || !file.type.startsWith('image/')) return
  const reader = new FileReader()
  reader.onload = () => {
    const src = String(reader.result || '')
    if (!src) return
    run((ed) => ed.chain().focus().setImage({ src }).run())
  }
  reader.readAsDataURL(file)
}

function addYoutube() {
  const url = window.prompt('YouTube 视频链接')
  if (!url) return
  run((ed) => ed.commands.setYoutubeVideo({ src: url }))
}

function onFontFamilyChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  run((ed) => {
    if (!value) ed.chain().focus().unsetFontFamily().run()
    else ed.chain().focus().setFontFamily(value).run()
  })
}

function onFontSizeChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  run((ed) => {
    if (!value) ed.chain().focus().unsetFontSize().run()
    else ed.chain().focus().setFontSize(value).run()
  })
}

function clearFormatting() {
  run((ed) =>
    ed.chain().focus().unsetAllMarks().clearNodes().run(),
  )
}

function promptColumnWidth() {
  run((ed) => {
    if (!ed.isActive('table')) return
    const current = getSelectedColumnWidth(ed)
    const input = window.prompt('列宽（像素）', String(current ?? 120))
    if (input === null) return
    const width = Number.parseInt(input, 10)
    if (!Number.isFinite(width)) return
    setSelectedColumnWidth(ed, width)
  })
}

function promptRowHeight() {
  run((ed) => {
    if (!ed.isActive('table')) return
    const current = getSelectedRowHeight(ed)
    const input = window.prompt('行高（像素）', String(current ?? 36))
    if (input === null) return
    const height = Number.parseInt(input, 10)
    if (!Number.isFinite(height)) return
    setSelectedRowHeight(ed, height)
  })
}

function insertCodeBlock() {
  run((ed) => {
    ed.chain().focus().setCodeBlock({ language: 'javascript' }).run()
  })
}

function onCodeLanguageChange(event: Event) {
  const language = (event.target as HTMLSelectElement).value
  run((ed) => {
    ed.chain().focus().updateAttributes('codeBlock', { language }).run()
  })
}

function openSourceMode() {
  if (!editor.value || sourceMode.value) return
  sourceHtml.value = editor.value.getHTML()
  sourceMode.value = true
}

function applySourceMode() {
  if (!editor.value) return
  editor.value.commands.setContent(sourceHtml.value)
  emit('update:modelValue', sourceHtml.value)
  sourceMode.value = false
}

function cancelSourceMode() {
  sourceMode.value = false
}

function onSourceInput() {
  emit('update:modelValue', sourceHtml.value)
}

defineExpose({
  editor,
  sourceMode,
  getHTML: () => (sourceMode.value ? sourceHtml.value : editor.value?.getHTML() ?? ''),
  getJSON: () => editor.value?.getJSON(),
  setContent: (html: string) => {
    if (sourceMode.value) {
      sourceHtml.value = html
      return
    }
    editor.value?.commands.setContent(html)
  },
})
</script>

<template>
  <div v-if="editor" class="tiptap-shell">
    <div class="toolbar" role="toolbar" aria-label="编辑工具栏">
      <div class="group">
        <button type="button" title="撤销" :disabled="!editor.can().undo()" @click="editor.chain().focus().undo().run()">↶</button>
        <button type="button" title="重做" :disabled="!editor.can().redo()" @click="editor.chain().focus().redo().run()">↷</button>
      </div>

      <div class="group">
        <select class="select" title="字体" @change="onFontFamilyChange">
          <option v-for="f in FONT_FAMILIES" :key="f.label" :value="f.value">{{ f.label }}</option>
        </select>
        <select class="select narrow" title="字号" @change="onFontSizeChange">
          <option value="">字号</option>
          <option v-for="s in FONT_SIZES" :key="s" :value="s">{{ s }}</option>
        </select>
      </div>

      <div class="group">
        <button type="button" title="段落" :class="{ active: editor.isActive('paragraph') }" @click="editor.chain().focus().setParagraph().run()">¶</button>
        <button type="button" title="标题 1" :class="{ active: editor.isActive('heading', { level: 1 }) }" @click="editor.chain().focus().toggleHeading({ level: 1 }).run()">H1</button>
        <button type="button" title="标题 2" :class="{ active: editor.isActive('heading', { level: 2 }) }" @click="editor.chain().focus().toggleHeading({ level: 2 }).run()">H2</button>
        <button type="button" title="标题 3" :class="{ active: editor.isActive('heading', { level: 3 }) }" @click="editor.chain().focus().toggleHeading({ level: 3 }).run()">H3</button>
        <button type="button" title="标题 4" :class="{ active: editor.isActive('heading', { level: 4 }) }" @click="editor.chain().focus().toggleHeading({ level: 4 }).run()">H4</button>
      </div>

      <div class="group">
        <button type="button" title="粗体" :class="{ active: editor.isActive('bold') }" @click="editor.chain().focus().toggleBold().run()"><b>B</b></button>
        <button type="button" title="斜体" :class="{ active: editor.isActive('italic') }" @click="editor.chain().focus().toggleItalic().run()"><i>I</i></button>
        <button type="button" title="下划线" :class="{ active: editor.isActive('underline') }" @click="editor.chain().focus().toggleUnderline().run()"><u>U</u></button>
        <button type="button" title="删除线" :class="{ active: editor.isActive('strike') }" @click="editor.chain().focus().toggleStrike().run()"><s>S</s></button>
        <button type="button" title="行内代码" :class="{ active: editor.isActive('code') }" @click="editor.chain().focus().toggleCode().run()">{ }</button>
        <button type="button" title="下标" :class="{ active: editor.isActive('subscript') }" @click="editor.chain().focus().toggleSubscript().run()">X₂</button>
        <button type="button" title="上标" :class="{ active: editor.isActive('superscript') }" @click="editor.chain().focus().toggleSuperscript().run()">X²</button>
      </div>

      <div class="group color-group">
        <label class="color-label" title="文字颜色">
          A
          <input
            type="color"
            :value="editor.getAttributes('textStyle').color || '#000000'"
            @input="editor.chain().focus().setColor(($event.target as HTMLInputElement).value).run()"
          />
        </label>
        <div class="swatches" title="常用文字色">
          <button
            v-for="c in COLORS"
            :key="'fg-' + c"
            type="button"
            class="swatch"
            :style="{ background: c }"
            @click="editor.chain().focus().setColor(c).run()"
          />
        </div>
        <button type="button" title="清除颜色" @click="editor.chain().focus().unsetColor().run()">⊘</button>
      </div>

      <div class="group color-group">
        <label class="color-label" title="背景色">
          ▮
          <input
            type="color"
            :value="editor.getAttributes('textStyle').backgroundColor || '#ffffff'"
            @input="editor.chain().focus().setBackgroundColor(($event.target as HTMLInputElement).value).run()"
          />
        </label>
        <div class="swatches" title="高亮">
          <button
            v-for="c in HIGHLIGHTS"
            :key="'hl-' + c"
            type="button"
            class="swatch"
            :style="{ background: c }"
            :class="{ active: editor.isActive('highlight', { color: c }) }"
            @click="editor.chain().focus().toggleHighlight({ color: c }).run()"
          />
        </div>
        <button type="button" title="清除高亮" @click="editor.chain().focus().unsetHighlight().run()">⊘</button>
      </div>

      <div class="group">
        <button type="button" title="左对齐" :class="{ active: editor.isActive({ textAlign: 'left' }) }" @click="editor.chain().focus().setTextAlign('left').run()">⬅</button>
        <button type="button" title="居中" :class="{ active: editor.isActive({ textAlign: 'center' }) }" @click="editor.chain().focus().setTextAlign('center').run()">⬌</button>
        <button type="button" title="右对齐" :class="{ active: editor.isActive({ textAlign: 'right' }) }" @click="editor.chain().focus().setTextAlign('right').run()">➡</button>
        <button type="button" title="两端对齐" :class="{ active: editor.isActive({ textAlign: 'justify' }) }" @click="editor.chain().focus().setTextAlign('justify').run()">☰</button>
      </div>

      <div class="group">
        <button type="button" title="无序列表" :class="{ active: editor.isActive('bulletList') }" @click="editor.chain().focus().toggleBulletList().run()">• 列表</button>
        <button type="button" title="有序列表" :class="{ active: editor.isActive('orderedList') }" @click="editor.chain().focus().toggleOrderedList().run()">1. 列表</button>
        <button type="button" title="任务列表" :class="{ active: editor.isActive('taskList') }" @click="editor.chain().focus().toggleTaskList().run()">☑</button>
        <button type="button" title="引用" :class="{ active: editor.isActive('blockquote') }" @click="editor.chain().focus().toggleBlockquote().run()">❝</button>
        <button type="button" title="代码块" :class="{ active: editor.isActive('codeBlock') }" @click="insertCodeBlock">&lt;/&gt;</button>
        <select
          class="select"
          title="代码语言"
          :disabled="!editor.isActive('codeBlock') || sourceMode"
          :value="currentCodeLanguage"
          @change="onCodeLanguageChange"
        >
          <option v-for="lang in CODE_LANGUAGES" :key="lang.value" :value="lang.value">
            {{ lang.label }}
          </option>
        </select>
        <button type="button" title="分割线" @click="editor.chain().focus().setHorizontalRule().run()">―</button>
        <button type="button" title="硬换行" @click="editor.chain().focus().setHardBreak().run()">↵</button>
      </div>

      <div class="group">
        <button type="button" title="链接" :class="{ active: editor.isActive('link') }" @click="setLink">链接</button>
        <button type="button" title="图片 URL" @click="addImageByUrl">图片</button>
        <label class="file-btn" title="本地图片">
          上传图
          <input type="file" accept="image/*" @change="onImageFileChange" />
        </label>
        <button type="button" title="YouTube" @click="addYoutube">YT</button>
      </div>

      <div class="group">
        <button type="button" title="插入表格" @click="editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()">表格</button>
        <button type="button" title="前插列" :disabled="!editor.can().addColumnBefore()" @click="editor.chain().focus().addColumnBefore().run()">+|</button>
        <button type="button" title="后插列" :disabled="!editor.can().addColumnAfter()" @click="editor.chain().focus().addColumnAfter().run()">|+</button>
        <button type="button" title="删列" :disabled="!editor.can().deleteColumn()" @click="editor.chain().focus().deleteColumn().run()">-|</button>
        <button type="button" title="上插行" :disabled="!editor.can().addRowBefore()" @click="editor.chain().focus().addRowBefore().run()">+━</button>
        <button type="button" title="下插行" :disabled="!editor.can().addRowAfter()" @click="editor.chain().focus().addRowAfter().run()">━+</button>
        <button type="button" title="删行" :disabled="!editor.can().deleteRow()" @click="editor.chain().focus().deleteRow().run()">-━</button>
        <button type="button" title="合并/拆分" :disabled="!editor.can().mergeOrSplit()" @click="editor.chain().focus().mergeOrSplit().run()">合并</button>
        <button type="button" title="表头行" :disabled="!editor.can().toggleHeaderRow()" @click="editor.chain().focus().toggleHeaderRow().run()">表头</button>
        <button type="button" title="设置列宽" :disabled="!editor.isActive('table')" @click="promptColumnWidth">列宽</button>
        <button type="button" title="设置行高" :disabled="!editor.isActive('table')" @click="promptRowHeight">行高</button>
        <button type="button" title="删除表格" :disabled="!editor.can().deleteTable()" @click="editor.chain().focus().deleteTable().run()">删表</button>
      </div>

      <div class="group">
        <button type="button" title="清除格式" @click="clearFormatting">清除格式</button>
      </div>

      <div class="group">
        <button
          v-if="!sourceMode"
          type="button"
          title="HTML 源码编辑"
          @click="openSourceMode"
        >
          源码
        </button>
        <template v-else>
          <button type="button" class="active" title="应用源码并返回可视化" @click="applySourceMode">应用</button>
          <button type="button" title="取消源码编辑" @click="cancelSourceMode">取消</button>
        </template>
      </div>
    </div>

    <textarea
      v-if="sourceMode"
      v-model="sourceHtml"
      class="source-editor"
      spellcheck="false"
      aria-label="HTML 源码"
      @input="onSourceInput"
    />
    <EditorContent v-else :editor="editor" class="editor-surface" />

    <footer class="status">
      <span v-if="sourceMode">源码模式 · HTML</span>
      <template v-else>
        <span>{{ wordCount }} 词</span>
        <span>{{ charCount }} 字</span>
      </template>
    </footer>
  </div>
</template>

<style scoped>
.tiptap-shell {
  display: flex;
  flex-direction: column;
  min-height: 0;
  height: 100%;
  border: 1px solid #d8dee8;
  border-radius: 8px;
  background: #fff;
  overflow: hidden;
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  padding: 0.55rem 0.65rem;
  border-bottom: 1px solid #e5e8ef;
  background: #f7f8fb;
}

.group {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.2rem;
  padding-right: 0.45rem;
  border-right: 1px solid #e1e5ee;
}

.group:last-child {
  border-right: none;
}

.toolbar button,
.file-btn,
.select {
  height: 28px;
  padding: 0 0.45rem;
  border: 1px solid #d0d5e0;
  border-radius: 4px;
  background: #fff;
  color: #243047;
  font-size: 0.78rem;
  cursor: pointer;
}

.toolbar button:hover:not(:disabled),
.file-btn:hover,
.select:hover {
  background: #eef2fa;
}

.toolbar button.active {
  border-color: #2f6fed;
  background: #e8f0ff;
  color: #1d4fc4;
}

.toolbar button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.select {
  max-width: 7.5rem;
}

.select.narrow {
  max-width: 4.5rem;
}

.color-group {
  gap: 0.35rem;
}

.color-label {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: 1px solid #d0d5e0;
  border-radius: 4px;
  background: #fff;
  font-weight: 700;
  cursor: pointer;
  overflow: hidden;
}

.color-label input {
  position: absolute;
  inset: 0;
  opacity: 0;
  cursor: pointer;
}

.swatches {
  display: flex;
  gap: 2px;
}

.swatch {
  width: 16px !important;
  height: 16px !important;
  padding: 0 !important;
  border-radius: 2px !important;
}

.swatch.active {
  outline: 2px solid #2f6fed;
}

.file-btn {
  display: inline-flex;
  align-items: center;
  position: relative;
  overflow: hidden;
}

.file-btn input {
  position: absolute;
  inset: 0;
  opacity: 0;
  cursor: pointer;
}

.editor-surface {
  flex: 1 1 auto;
  min-height: 360px;
  overflow: auto;
  padding: 1rem 1.25rem 2rem;
}

.source-editor {
  flex: 1 1 auto;
  min-height: 360px;
  width: 100%;
  margin: 0;
  padding: 1rem 1.25rem;
  border: none;
  outline: none;
  resize: none;
  background: #0f172a;
  color: #e2e8f0;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 0.85rem;
  line-height: 1.6;
  tab-size: 2;
}

.status {
  display: flex;
  justify-content: flex-end;
  gap: 1rem;
  padding: 0.4rem 0.85rem;
  border-top: 1px solid #e5e8ef;
  background: #fafbfd;
  color: #6b7385;
  font-size: 0.75rem;
}

:deep(.tiptap-prose) {
  outline: none;
  min-height: 320px;
  line-height: 1.65;
  color: #1f2937;
}

:deep(.tiptap-prose > * + *) {
  margin-top: 0.65rem;
}

:deep(.tiptap-prose h1) {
  font-size: 1.75rem;
  font-weight: 700;
  line-height: 1.3;
}

:deep(.tiptap-prose h2) {
  font-size: 1.4rem;
  font-weight: 700;
}

:deep(.tiptap-prose h3) {
  font-size: 1.2rem;
  font-weight: 650;
}

:deep(.tiptap-prose h4) {
  font-size: 1.05rem;
  font-weight: 650;
}

:deep(.tiptap-prose ul),
:deep(.tiptap-prose ol) {
  padding-left: 1.4rem;
}

:deep(.tiptap-prose ul[data-type='taskList']) {
  list-style: none;
  padding-left: 0.15rem;
}

:deep(.tiptap-prose ul[data-type='taskList'] li) {
  display: flex;
  align-items: flex-start;
  gap: 0.45rem;
}

:deep(.tiptap-prose ul[data-type='taskList'] li label) {
  margin-top: 0.2rem;
}

:deep(.tiptap-prose blockquote) {
  margin: 0;
  padding: 0.25rem 0.85rem;
  border-left: 3px solid #2f6fed;
  color: #4b5563;
  background: #f8fafc;
}

:deep(.tiptap-prose hr) {
  border: none;
  border-top: 1px solid #d1d5db;
  margin: 1.25rem 0;
}

:deep(.tiptap-prose code) {
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background: #f1f5f9;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 0.9em;
}

:deep(.tiptap-prose pre) {
  margin: 0;
  padding: 0.85rem 1rem;
  border-radius: 8px;
  background: #0f172a;
  color: #e2e8f0;
  overflow-x: auto;
}

:deep(.tiptap-prose pre code) {
  padding: 0;
  background: transparent;
  color: inherit;
}

:deep(.tiptap-prose img) {
  max-width: 100%;
  height: auto;
  border-radius: 6px;
}

:deep(.tiptap-prose a) {
  color: #2f6fed;
  text-decoration: underline;
}

:deep(.tiptap-prose table) {
  border-collapse: collapse;
  table-layout: fixed;
  margin: 0.75rem 0;
  overflow: visible;
  width: auto;
  max-width: 100%;
}

:deep(.tiptap-prose td),
:deep(.tiptap-prose th) {
  border: 1px solid #cbd5e1;
  padding: 0.45rem 0.55rem;
  vertical-align: top;
  min-width: 40px;
  position: relative;
  box-sizing: border-box;
}

:deep(.tiptap-prose th) {
  background: #f1f5f9;
  font-weight: 600;
}

:deep(.tiptap-prose .selectedCell::after) {
  content: '';
  position: absolute;
  inset: 0;
  background: rgba(47, 111, 237, 0.12);
  pointer-events: none;
  z-index: 1;
}

:deep(.tiptap-prose .column-resize-handle) {
  position: absolute;
  right: -2px;
  top: 0;
  bottom: -2px;
  width: 5px;
  background: #2f6fed;
  pointer-events: none;
  z-index: 2;
}

:deep(.tiptap-prose .tableWrapper) {
  overflow-x: auto;
  width: 100%;
  margin: 0.75rem 0;
  padding-bottom: 4px;
}

:deep(.tiptap-prose .tableWrapper table) {
  margin: 0;
}

:deep(.tiptap-prose.resize-cursor) {
  cursor: col-resize;
}

:deep(.tiptap-prose.row-resize-cursor) {
  cursor: row-resize;
}

:deep(.tiptap-prose iframe) {
  max-width: 100%;
  border-radius: 8px;
}

:deep(.tiptap-prose p.is-editor-empty:first-child::before) {
  content: attr(data-placeholder);
  float: left;
  color: #9aa3b5;
  pointer-events: none;
  height: 0;
}

:deep(.hljs-comment),
:deep(.hljs-quote) {
  color: #94a3b8;
}

:deep(.hljs-keyword),
:deep(.hljs-selector-tag),
:deep(.hljs-addition) {
  color: #c4b5fd;
}

:deep(.hljs-number),
:deep(.hljs-string),
:deep(.hljs-meta .hljs-meta-string),
:deep(.hljs-literal),
:deep(.hljs-doctag),
:deep(.hljs-regexp) {
  color: #86efac;
}

:deep(.hljs-title),
:deep(.hljs-section),
:deep(.hljs-name),
:deep(.hljs-selector-id),
:deep(.hljs-selector-class) {
  color: #93c5fd;
}

:deep(.hljs-attribute),
:deep(.hljs-attr),
:deep(.hljs-variable),
:deep(.hljs-template-variable),
:deep(.hljs-class .hljs-title),
:deep(.hljs-type) {
  color: #fcd34d;
}

:deep(.hljs-symbol),
:deep(.hljs-bullet),
:deep(.hljs-subst),
:deep(.hljs-meta),
:deep(.hljs-meta .hljs-keyword),
:deep(.hljs-selector-attr),
:deep(.hljs-selector-pseudo),
:deep(.hljs-link) {
  color: #fda4af;
}

:deep(.hljs-built_in),
:deep(.hljs-deletion) {
  color: #fdba74;
}
</style>

<style>
.table-row-resize-guide {
  position: fixed;
  left: 0;
  right: 0;
  height: 2px;
  background: #2f6fed;
  pointer-events: none;
  z-index: 10000;
}
</style>
