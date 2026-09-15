<script setup lang="ts">
import type { FUniver, IDisposable, IWorkbookData, Univer } from '@univerjs/presets'
import { UniverSheetsCorePreset } from '@univerjs/preset-sheets-core'
import UniverPresetSheetsCoreZhCN from '@univerjs/preset-sheets-core/locales/zh-CN'
import { CommandType, createUniver, LocaleType, mergeLocales } from '@univerjs/presets'
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { emptyWorkbook, emptyWorkbookJson } from '../utils/emptyWorkbook'

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
/**
 * Nested load/create suppression depth. Change emits only when depth === 0.
 * Avoids races where a second load clears a boolean while the first is still settling.
 */
let suppressChangeDepth = 0

function beginSuppressChange() {
  suppressChangeDepth += 1
}

function endSuppressChange() {
  // Defer so createWorkbook's own mutations do not mark dirty.
  queueMicrotask(() => {
    suppressChangeDepth = Math.max(0, suppressChangeDepth - 1)
  })
}

function parseWorkbookData(json: string | null | undefined): Partial<IWorkbookData> {
  if (json == null || json.trim() === '') {
    return emptyWorkbook()
  }
  try {
    return JSON.parse(json) as Partial<IWorkbookData>
  } catch (err) {
    console.warn('[UniverSheetHost] invalid workbookJson; falling back to emptyWorkbook()', err)
    return emptyWorkbook()
  }
}

function getWorkbookJson(): string {
  const snapshot = univerAPIInstance?.getActiveWorkbook()?.save()
  if (!snapshot) {
    return emptyWorkbookJson()
  }
  return JSON.stringify(snapshot)
}

function loadWorkbookJson(json: string) {
  if (!univerAPIInstance) return
  beginSuppressChange()
  try {
    const active = univerAPIInstance.getActiveWorkbook()
    const unitId = active?.getId()
    if (unitId) {
      univerAPIInstance.disposeUnit(unitId)
    }
    univerAPIInstance.createWorkbook(parseWorkbookData(json))
  } finally {
    endSuppressChange()
  }
}

function attachChangeListener(api: FUniver) {
  // Prefer CommandExecuted + MUTATION: only snapshot-affecting edits (not scroll/selection).
  // If MUTATION filtering proves too noisy/quiet in practice, fall back to emitting on all commands.
  commandDisposable = api.addEvent(api.Event.CommandExecuted, (event) => {
    if (suppressChangeDepth !== 0) return
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

  beginSuppressChange()
  try {
    univerAPI.createWorkbook(parseWorkbookData(props.workbookJson))
  } finally {
    endSuppressChange()
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
