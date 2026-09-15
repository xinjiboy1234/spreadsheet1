<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const documentId = computed(() => String(route.params.id ?? ''))
</script>

<template>
  <main class="page">
    <h1>版本历史</h1>
    <p>文档 ID：{{ documentId }}</p>
    <p>暂未实现版本列表。</p>
  </main>
</template>

<style scoped>
.page {
  padding: 1.5rem;
}
</style>
