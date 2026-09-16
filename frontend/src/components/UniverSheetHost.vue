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

function setGridlinesVisible(visible: boolean) {
  const workbook = univerAPIInstance?.getActiveWorkbook()
  if (!workbook) return
  const hidden = !visible
  for (const sheet of workbook.getSheets()) {
    sheet.setHiddenGridlines(hidden)
  }
}

type HtmlCapableRange = {
  getA1Notation: () => string
  getRow: () => number
  getColumn: () => number
  getLastRow: () => number
  getLastColumn: () => number
  getDataRegion: () => HtmlCapableRange
  generateHTML: () => string
}

/**
 * High-fidelity HTML via Univer clipboard USM→HTML (styles, merges, col widths).
 * Uses the active selection; expands a single cell to its data region.
 */
function getRangeHtml(): { html: string; rangeA1: string } {
  const workbook = univerAPIInstance?.getActiveWorkbook()
  const sheet = workbook?.getActiveSheet()
  if (!sheet) {
    throw new Error('当前没有可用工作表')
  }

  let range = sheet.getActiveRange() as HtmlCapableRange | null
  if (!range) {
    throw new Error('请先选中要导出的单元格区域')
  }

  const isSingleCell =
    range.getRow() === range.getLastRow() && range.getColumn() === range.getLastColumn()
  if (isSingleCell) {
    range = range.getDataRegion()
  }

  if (typeof range.generateHTML !== 'function') {
    throw new Error('当前 Univer 版本不支持 generateHTML')
  }

  const html = range.generateHTML()
  if (!html.trim()) {
    throw new Error('选中区域没有可导出的内容')
  }

  return { html, rangeA1: range.getA1Notation() }
}

function loadWorkbookJson(json: string, gridlinesVisible = false) {
  if (!univerAPIInstance) return
  beginSuppressChange()
  try {
    const active = univerAPIInstance.getActiveWorkbook()
    const unitId = active?.getId()
    if (unitId) {
      univerAPIInstance.disposeUnit(unitId)
    }
    univerAPIInstance.createWorkbook(parseWorkbookData(json))
    setGridlinesVisible(gridlinesVisible)
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
    // Default: hide gridlines (toggle in editor can turn them on).
    setGridlinesVisible(false)
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
  setGridlinesVisible,
  getRangeHtml,
})
</script>

<template>
  <div ref="container" class="univer-host" />
</template>

<style scoped>
.univer-host {
  width: 100%;
  height: 100%;
  min-height: 0;
}
</style>
