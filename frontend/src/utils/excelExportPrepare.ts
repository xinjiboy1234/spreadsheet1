/**
 * Normalize a Univer workbook snapshot before LuckyExcel export.
 *
 * LuckyExcel's Univer→Excel path still uses LuckySheet alignment enums and
 * drops non-master borders when merging cells. Defined names imported with
 * CDATA wrappers also produce workbooks Excel marks as corrupt.
 */

type BorderSide = { s?: number; cl?: { rgb?: string; th?: number } } | null | undefined

type StyleLike = {
  ht?: number
  vt?: number
  bd?: {
    t?: BorderSide
    b?: BorderSide
    l?: BorderSide
    r?: BorderSide
    tl_br?: BorderSide
    bl_tr?: BorderSide
  } | null
  [key: string]: unknown
}

type CellLike = {
  s?: string | StyleLike
  [key: string]: unknown
}

type MergeRange = {
  startRow: number
  endRow: number
  startColumn: number
  endColumn: number
}

type SheetLike = {
  cellData?: Record<string, Record<string, CellLike | null | undefined>>
  mergeData?: MergeRange[]
  [key: string]: unknown
}

type Snapshot = {
  styles?: Record<string, StyleLike>
  sheets?: Record<string, SheetLike>
  resources?: Array<{ name?: string; data?: string }>
  [key: string]: unknown
}

/** Univer HorizontalAlign → LuckySheet ht expected by LuckyExcel export. */
const HT_UNIVER_TO_LUCKY: Record<number, number> = {
  1: 1, // LEFT → left
  2: 0, // CENTER → center
  3: 2, // RIGHT → right
  4: 3, // JUSTIFIED → justify
  5: 3, // BOTH → justify
  6: 3, // DISTRIBUTED → justify
}

/** Univer VerticalAlign → LuckySheet vt expected by LuckyExcel export. */
const VT_UNIVER_TO_LUCKY: Record<number, number> = {
  1: 1, // TOP → top
  2: 0, // MIDDLE → middle
  3: 2, // BOTTOM → bottom
}

const BORDER_SIDES = ['t', 'b', 'l', 'r', 'tl_br', 'bl_tr'] as const

function isActiveBorder(side: BorderSide): side is NonNullable<BorderSide> {
  return !!side && typeof side === 'object' && side.s !== undefined && side.s !== 0
}

function cleanBorder(bd: StyleLike['bd']): StyleLike['bd'] | undefined {
  if (!bd || typeof bd !== 'object') return undefined
  const next: NonNullable<StyleLike['bd']> = {}
  let any = false
  for (const side of BORDER_SIDES) {
    const value = bd[side]
    if (isActiveBorder(value)) {
      next[side] = value
      any = true
    }
  }
  return any ? next : undefined
}

function remapAlignment(style: StyleLike): void {
  if (style.ht !== undefined) {
    const mapped = HT_UNIVER_TO_LUCKY[style.ht]
    if (mapped === undefined) delete style.ht
    else style.ht = mapped
  }
  if (style.vt !== undefined) {
    const mapped = VT_UNIVER_TO_LUCKY[style.vt]
    if (mapped === undefined) delete style.vt
    else style.vt = mapped
  }
}

function normalizeStyle(style: StyleLike): void {
  remapAlignment(style)
  if (style.bd) {
    const cleaned = cleanBorder(style.bd)
    if (cleaned) style.bd = cleaned
    else delete style.bd
  }
}

function resolveStyle(
  cell: CellLike,
  styles: Record<string, StyleLike>,
): StyleLike | undefined {
  if (!cell.s) return undefined
  if (typeof cell.s === 'string') return styles[cell.s]
  return cell.s
}

function cloneStyle(style: StyleLike | undefined): StyleLike {
  return style ? (JSON.parse(JSON.stringify(style)) as StyleLike) : {}
}

function mergeBorderOntoMaster(
  masterBd: NonNullable<StyleLike['bd']>,
  otherBd: NonNullable<StyleLike['bd']>,
  merge: MergeRange,
  row: number,
  col: number,
): void {
  // Outer edges of the merged range must survive on the master cell.
  if (row === merge.startRow && isActiveBorder(otherBd.t)) masterBd.t = otherBd.t
  if (row === merge.endRow && isActiveBorder(otherBd.b)) masterBd.b = otherBd.b
  if (col === merge.startColumn && isActiveBorder(otherBd.l)) masterBd.l = otherBd.l
  if (col === merge.endColumn && isActiveBorder(otherBd.r)) masterBd.r = otherBd.r
  if (isActiveBorder(otherBd.tl_br)) masterBd.tl_br = otherBd.tl_br
  if (isActiveBorder(otherBd.bl_tr)) masterBd.bl_tr = otherBd.bl_tr
}

function consolidateMergeBorders(snapshot: Snapshot): void {
  const styles = snapshot.styles ?? (snapshot.styles = {})
  const sheets = snapshot.sheets ?? {}

  for (const sheet of Object.values(sheets)) {
    const merges = sheet.mergeData
    if (!merges?.length) continue
    const cellData = sheet.cellData ?? (sheet.cellData = {})

    for (const merge of merges) {
      const { startRow, endRow, startColumn, endColumn } = merge
      if (
        startRow === undefined ||
        endRow === undefined ||
        startColumn === undefined ||
        endColumn === undefined
      ) {
        continue
      }

      cellData[startRow] ??= {}
      const masterCell = cellData[startRow][startColumn] ?? (cellData[startRow][startColumn] = {})
      const masterStyle = cloneStyle(resolveStyle(masterCell, styles))
      const consolidatedBd = { ...(masterStyle.bd ?? {}) }

      for (let row = startRow; row <= endRow; row++) {
        for (let col = startColumn; col <= endColumn; col++) {
          if (row === startRow && col === startColumn) continue
          const cell = cellData[row]?.[col]
          if (!cell) continue
          const style = resolveStyle(cell, styles)
          if (!style?.bd) continue
          mergeBorderOntoMaster(consolidatedBd, style.bd, merge, row, col)
        }
      }

      const cleaned = cleanBorder(consolidatedBd)
      if (cleaned) masterStyle.bd = cleaned
      else delete masterStyle.bd

      // Always assign a dedicated style so shared style ids are not mutated.
      const styleId = `export_merge_${startRow}_${startColumn}_${endRow}_${endColumn}`
      styles[styleId] = masterStyle
      masterCell.s = styleId
    }
  }
}

function stripCdata(value: string): string {
  const trimmed = value.trim()
  const match = trimmed.match(/^<!\[CDATA\[(.*)\]\]>$/s)
  return match ? match[1] : trimmed
}

function isSafeDefinedName(name: string): boolean {
  if (!name || name.length > 255) return false
  // ExcelJS corrupts built-in print/filter names when re-added.
  if (/^_xlnm\./i.test(name)) return false
  // Excel disallows names that look like cell refs (A1) and most punctuation.
  if (/^[A-Za-z]+\d+$/.test(name)) return false
  if (/[<>[\]*?:/\\]/.test(name)) return false
  // Allow ASCII and CJK named ranges commonly present in imported workbooks.
  return /^[\p{L}\p{N}_.]+$/u.test(name)
}

function isSafeDefinedFormula(formula: string): boolean {
  if (!formula) return false
  if (/#REF!|#NAME\?|#VALUE!|#DIV\/0!|#NULL!|#NUM!|#N\/A/i.test(formula)) return false
  // Drop external-workbook refs and leftover markup — common repair triggers.
  if (/[[\]<>]|<!\[CDATA/i.test(formula)) return false
  // Bare numeric constants make ExcelJS emit corrupt defined-name records.
  if (/^[0-9.]+$/.test(formula)) return false
  // Keep sheet refs / real formulas only.
  return /!|[A-Za-z_][\w.]*\s*\(/.test(formula)
}

function sanitizeDefinedNames(snapshot: Snapshot): void {
  const resources = snapshot.resources
  if (!Array.isArray(resources)) return

  for (const resource of resources) {
    if (resource.name !== 'SHEET_DEFINED_NAME_PLUGIN' || !resource.data) continue
    try {
      const parsed = JSON.parse(resource.data) as Record<
        string,
        { name?: string; formulaOrRefString?: string; [key: string]: unknown }
      >
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        resource.data = '{}'
        continue
      }

      const cleaned: typeof parsed = {}
      for (const [key, entry] of Object.entries(parsed)) {
        if (!entry?.name || typeof entry.formulaOrRefString !== 'string') continue
        const formula = stripCdata(entry.formulaOrRefString)
        if (!isSafeDefinedName(entry.name) || !isSafeDefinedFormula(formula)) continue
        cleaned[key] = { ...entry, formulaOrRefString: formula }
      }
      resource.data = JSON.stringify(cleaned)
    } catch {
      resource.data = '{}'
    }
  }
}

function normalizeAllStyles(snapshot: Snapshot): void {
  if (snapshot.styles) {
    for (const style of Object.values(snapshot.styles)) {
      if (style && typeof style === 'object') normalizeStyle(style)
    }
  }

  for (const sheet of Object.values(snapshot.sheets ?? {})) {
    const cellData = sheet.cellData
    if (!cellData) continue
    for (const row of Object.values(cellData)) {
      if (!row) continue
      for (const cell of Object.values(row)) {
        if (cell?.s && typeof cell.s === 'object') normalizeStyle(cell.s)
      }
    }
  }
}

/** Deep-clone and fix a Univer snapshot for LuckyExcel export. */
export function prepareSnapshotForExport(snapshot: object): object {
  const prepared = JSON.parse(JSON.stringify(snapshot)) as Snapshot
  // Merge borders first (while ht/vt still use Univer enums on styles we clone).
  consolidateMergeBorders(prepared)
  normalizeAllStyles(prepared)
  sanitizeDefinedNames(prepared)
  return prepared
}
