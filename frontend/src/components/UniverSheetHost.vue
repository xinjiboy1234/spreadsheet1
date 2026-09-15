<script setup lang="ts">
import type { FUniver, IDisposable, IWorkbookData, Univer } from '@univerjs/presets'
import { UniverSheetsCorePreset } from '@univerjs/preset-sheets-core'
import UniverPresetSheetsCoreZhCN from '@univerjs/preset-sheets-core/locales/zh-CN'
import { CommandType, createUniver, LocaleType, mergeLocales } from '@univerjs/presets'
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { emptyWorkbook } from '../utils/emptyWorkbook'

import '@univerjs/preset-sheets-core/lib/index.css'

const props = defineProps<{
  workbookJson?: string | null
}>()

const emit = defineEmits<{
  change: []
}>()

const container = ref<HTMLElement | null>(null)

// Keep Univer instances out of Vue reactive proxies (official Vue integration note).
let univerInstance: Univer | null = null
let univerAPIInstance: FUniver | null = null
let commandDisposable: IDisposable | null = null
/** Suppress change emit while applying programmatic load/create. */
let suppressChange = false

function parseWorkbookData(json: string | null | undefined): Partial<IWorkbookData> {
  if (json == null || json.trim() === '') {
    return emptyWorkbook()
  }
  return JSON.parse(json) as Partial<IWorkbookData>
}

function getWorkbookJson(): string {
  const snapshot = univerAPIInstance?.getActiveWorkbook()?.save()
  if (!snapshot) {
    return emptyWorkbookJsonFallback()
  }
  return JSON.stringify(snapshot)
}

function emptyWorkbookJsonFallback(): string {
  return JSON.stringify(emptyWorkbook())
}

function loadWorkbookJson(json: string) {
  if (!univerAPIInstance) return
  suppressChange = true
  try {
    const active = univerAPIInstance.getActiveWorkbook()
    const unitId = active?.getId()
    if (unitId) {
      univerAPIInstance.disposeUnit(unitId)
    }
    univerAPIInstance.createWorkbook(parseWorkbookData(json))
  } finally {
    // Defer so createWorkbook's own mutations do not mark dirty.
    queueMicrotask(() => {
      suppressChange = false
    })
  }
}

function attachChangeListener(api: FUniver) {
  // Prefer CommandExecuted + MUTATION: only snapshot-affecting edits (not scroll/selection).
  // If MUTATION filtering proves too noisy/quiet in practice, fall back to emitting on all commands.
  commandDisposable = api.addEvent(api.Event.CommandExecuted, (event) => {
    if (suppressChange) return
    if (event.type !== CommandType.MUTATION) return
    emit('change')
  })
}

onMounted(() => {
  if (!container.value) return

  const { univer, univerAPI } = createUniver({
    locale: LocaleType.ZH_CN,
    locales: {
      [LocaleType.ZH_CN]: mergeLocales(UniverPresetSheetsCoreZhCN),
    },
    presets: [
      UniverSheetsCorePreset({
        container: container.value,
      }),
    ],
  })

  univerInstance = univer
  univerAPIInstance = univerAPI
  attachChangeListener(univerAPI)

  suppressChange = true
  try {
    univerAPI.createWorkbook(parseWorkbookData(props.workbookJson))
  } finally {
    queueMicrotask(() => {
      suppressChange = false
    })
  }
})

onBeforeUnmount(() => {
  commandDisposable?.dispose()
  commandDisposable = null
  univerAPIInstance?.dispose()
  univerInstance?.dispose()
  univerAPIInstance = null
  univerInstance = null
})

defineExpose({
  getWorkbookJson,
  loadWorkbookJson,
})
</script>

<template>
  <div ref="container" class="univer-host" />
</template>

<style scoped>
.univer-host {
  width: 100%;
  height: 100%;
  min-height: 480px;
}
</style>
