<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import UniverSheetHost from '../components/UniverSheetHost.vue'
import { useEditorDirty } from '../composables/useEditorDirty'

const route = useRoute()
const documentId = computed(() => String(route.params.id ?? ''))
const { markTemplateEdit } = useEditorDirty()

function onSheetChange() {
  markTemplateEdit()
}
</script>

<template>
  <main class="editor-page">
    <header class="toolbar">
      <h1>文档编辑</h1>
      <p class="meta">文档 ID：{{ documentId }}</p>
    </header>
    <UniverSheetHost class="sheet" @change="onSheetChange" />
  </main>
</template>

<style scoped>
.editor-page {
  display: flex;
  flex-direction: column;
  height: 100vh;
  min-height: 0;
  padding: 0;
}

.toolbar {
  flex: 0 0 auto;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #e5e5e5;
}

.toolbar h1 {
  margin: 0;
  font-size: 1.1rem;
}

.meta {
  margin: 0.25rem 0 0;
  color: #666;
  font-size: 0.875rem;
}

.sheet {
  flex: 1 1 auto;
  min-height: 480px;
}
</style>
