<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const documentId = computed(() => String(route.params.id ?? ''))
</script>

<template>
  <main class="page">
    <h1>文档编辑</h1>
    <p>文档 ID：{{ documentId }}</p>
    <p>暂未实现 Univer 编辑器（Task 7）。</p>
  </main>
</template>

<style scoped>
.page {
  padding: 1.5rem;
}
</style>
