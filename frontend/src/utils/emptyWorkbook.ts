import type { IWorkbookData } from '@univerjs/presets'
import { LocaleType } from '@univerjs/presets'

/** Minimal valid Univer IWorkbookData-like snapshot for new documents. */
export function emptyWorkbook(): Partial<IWorkbookData> {
  const sheetId = 'sheet-1'
  return {
    id: 'workbook-1',
    name: 'Workbook',
    appVersion: '0.25.1',
    locale: LocaleType.ZH_CN,
    styles: {},
    sheetOrder: [sheetId],
    sheets: {
      [sheetId]: {
        id: sheetId,
        name: 'Sheet1',
        rowCount: 100,
        columnCount: 20,
        cellData: {},
      },
    },
  }
}

export function emptyWorkbookJson(): string {
  return JSON.stringify(emptyWorkbook())
}
