import { Extension, type Editor } from '@tiptap/core'
import { TableRow } from '@tiptap/extension-table'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'
import { CellSelection, TableMap, cellAround } from '@tiptap/pm/tables'
import type { Node as ProseMirrorNode, ResolvedPos } from '@tiptap/pm/model'

const MIN_COL_WIDTH = 40
const MIN_ROW_HEIGHT = 28
const ROW_EDGE = 6

export const ResizableTableRow = TableRow.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      height: {
        default: null,
        parseHTML: (element) => {
          const raw =
            element.getAttribute('data-height') ||
            element.style.height ||
            element.getAttribute('height')
          if (!raw) return null
          const value = Number.parseInt(raw, 10)
          return Number.isFinite(value) ? value : null
        },
        renderHTML: (attributes) => {
          if (!attributes.height) return {}
          return {
            'data-height': attributes.height,
            style: `height: ${attributes.height}px`,
            height: attributes.height,
          }
        },
      },
    }
  },
})

type DragState = {
  rowPos: number
  startY: number
  startHeight: number
} | null

const rowResizeKey = new PluginKey('tableRowResize')

function findTableRow($pos: ResolvedPos): { pos: number; node: ProseMirrorNode } | null {
  for (let depth = $pos.depth; depth > 0; depth -= 1) {
    const node = $pos.node(depth)
    if (node.type.name === 'tableRow') {
      return { pos: $pos.before(depth), node }
    }
  }
  return null
}

function rowFromCoords(view: EditorView, event: MouseEvent) {
  const coords = view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (!coords) return null

  const $pos = view.state.doc.resolve(coords.inside >= 0 ? coords.inside : coords.pos)
  const row = findTableRow($pos)
  if (!row) return null

  const dom = view.nodeDOM(row.pos)
  if (!(dom instanceof HTMLElement)) return null

  const rect = dom.getBoundingClientRect()
  const nearBottom = Math.abs(event.clientY - rect.bottom) <= ROW_EDGE
  if (!nearBottom) return null

  return {
    ...row,
    height: row.node.attrs.height ?? Math.round(rect.height),
  }
}

function updateRowHeight(view: EditorView, rowPos: number, height: number) {
  const node = view.state.doc.nodeAt(rowPos)
  if (!node || node.type.name !== 'tableRow') return

  const next = Math.max(MIN_ROW_HEIGHT, Math.round(height))
  if (node.attrs.height === next) return

  view.dispatch(
    view.state.tr.setNodeMarkup(rowPos, undefined, {
      ...node.attrs,
      height: next,
    }),
  )
}

export const TableRowResize = Extension.create({
  name: 'tableRowResize',

  addProseMirrorPlugins() {
    let drag: DragState = null
    let guide: HTMLDivElement | null = null

    const clearGuide = () => {
      guide?.remove()
      guide = null
    }

    const showGuide = (y: number) => {
      if (!guide) {
        guide = document.createElement('div')
        guide.className = 'table-row-resize-guide'
        document.body.appendChild(guide)
      }
      guide.style.top = `${y}px`
    }

    return [
      new Plugin({
        key: rowResizeKey,
        props: {
          handleDOMEvents: {
            mousemove(view, event) {
              if (drag) return false
              const hit = rowFromCoords(view, event)
              view.dom.classList.toggle('row-resize-cursor', Boolean(hit))
              return false
            },
            mouseleave(view) {
              if (!drag) view.dom.classList.remove('row-resize-cursor')
              return false
            },
            mousedown(view, event) {
              if (event.button !== 0) return false
              const hit = rowFromCoords(view, event)
              if (!hit) return false

              event.preventDefault()
              drag = {
                rowPos: hit.pos,
                startY: event.clientY,
                startHeight: hit.height,
              }
              view.dom.classList.add('row-resize-cursor')
              showGuide(event.clientY)

              const onMove = (e: MouseEvent) => {
                if (!drag) return
                showGuide(e.clientY)
                updateRowHeight(view, drag.rowPos, drag.startHeight + (e.clientY - drag.startY))
              }

              const onUp = () => {
                drag = null
                clearGuide()
                view.dom.classList.remove('row-resize-cursor')
                window.removeEventListener('mousemove', onMove)
                window.removeEventListener('mouseup', onUp)
              }

              window.addEventListener('mousemove', onMove)
              window.addEventListener('mouseup', onUp)
              return true
            },
          },
        },
        view() {
          return {
            destroy() {
              clearGuide()
            },
          }
        },
      }),
    ]
  },
})

function getCellSelection(editor: Editor): CellSelection | null {
  const { selection } = editor.state
  return selection instanceof CellSelection ? selection : null
}

function getActiveCell($from: ResolvedPos) {
  return cellAround($from)
}

function cellStartCol(map: TableMap, row: number, col: number): number {
  const index = row * map.width + col
  let start = col
  while (start > 0 && map.map[row * map.width + start - 1] === map.map[index]) {
    start -= 1
  }
  return start
}

/** Set width (px) for the column(s) covered by the current cell selection / cursor. */
export function setSelectedColumnWidth(editor: Editor, width: number): boolean {
  const nextWidth = Math.max(MIN_COL_WIDTH, Math.round(width))
  const { state, view } = editor
  const cellSelection = getCellSelection(editor)
  const $cell = cellSelection ? cellSelection.$anchorCell : getActiveCell(state.selection.$from)
  if (!$cell) return false

  const table = $cell.node(-1)
  const mapStart = $cell.start(-1)
  const map = TableMap.get(table)
  const rect = cellSelection
    ? map.rectBetween(cellSelection.$anchorCell.pos - mapStart, cellSelection.$headCell.pos - mapStart)
    : map.findCell($cell.pos - mapStart)

  let tr = state.tr
  const seen = new Set<number>()

  for (let row = 0; row < map.height; row += 1) {
    for (let col = rect.left; col < rect.right; col += 1) {
      const cellPos = mapStart + map.map[row * map.width + col]
      if (seen.has(cellPos)) continue
      seen.add(cellPos)

      const cell = state.doc.nodeAt(cellPos)
      if (!cell) continue

      const colspan: number = cell.attrs.colspan ?? 1
      const startCol = cellStartCol(map, row, col)
      const prev: number[] = Array.isArray(cell.attrs.colwidth)
        ? [...cell.attrs.colwidth]
        : Array.from({ length: colspan }, () => MIN_COL_WIDTH)

      while (prev.length < colspan) prev.push(MIN_COL_WIDTH)

      for (let i = 0; i < colspan; i += 1) {
        const absolute = startCol + i
        if (absolute >= rect.left && absolute < rect.right) {
          prev[i] = nextWidth
        }
      }

      tr = tr.setNodeMarkup(cellPos, undefined, {
        ...cell.attrs,
        colwidth: prev,
      })
    }
  }

  if (tr.docChanged) {
    view.dispatch(tr)
    return true
  }
  return false
}

/** Set height (px) for the row(s) covered by the current cell selection / cursor. */
export function setSelectedRowHeight(editor: Editor, height: number): boolean {
  const nextHeight = Math.max(MIN_ROW_HEIGHT, Math.round(height))
  const { state, view } = editor
  const cellSelection = getCellSelection(editor)
  const $cell = cellSelection ? cellSelection.$anchorCell : getActiveCell(state.selection.$from)
  if (!$cell) return false

  const table = $cell.node(-1)
  const mapStart = $cell.start(-1)
  const map = TableMap.get(table)
  const rect = cellSelection
    ? map.rectBetween(cellSelection.$anchorCell.pos - mapStart, cellSelection.$headCell.pos - mapStart)
    : map.findCell($cell.pos - mapStart)

  let tr = state.tr
  const seen = new Set<number>()

  for (let row = rect.top; row < rect.bottom; row += 1) {
    const cellPos = mapStart + map.map[row * map.width]
    const rowInfo = findTableRow(state.doc.resolve(cellPos))
    if (!rowInfo || seen.has(rowInfo.pos)) continue
    seen.add(rowInfo.pos)

    tr = tr.setNodeMarkup(rowInfo.pos, undefined, {
      ...rowInfo.node.attrs,
      height: nextHeight,
    })
  }

  if (tr.docChanged) {
    view.dispatch(tr)
    return true
  }
  return false
}

export function getSelectedColumnWidth(editor: Editor): number | null {
  const $cell = getCellSelection(editor)?.$anchorCell ?? getActiveCell(editor.state.selection.$from)
  if (!$cell) return null
  const cell = $cell.nodeAfter
  const width = cell?.attrs.colwidth?.[0]
  return typeof width === 'number' ? width : null
}

export function getSelectedRowHeight(editor: Editor): number | null {
  const $cell = getCellSelection(editor)?.$anchorCell ?? getActiveCell(editor.state.selection.$from)
  if (!$cell) return null
  const row = findTableRow($cell)
  const height = row?.node.attrs.height
  return typeof height === 'number' ? height : null
}
