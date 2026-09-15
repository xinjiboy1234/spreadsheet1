import LuckyExcel from '@mertdeveci55/univer-import-export'

/** Convert an Excel file to a Univer workbook snapshot object. */
export function importExcelFile(file: File): Promise<object> {
  return new Promise((resolve, reject) => {
    void LuckyExcel.transformExcelToUniver(
      file,
      (data) => {
        resolve(data as object)
      },
      (err) => {
        reject(err)
      },
    )
  })
}

/** Download a Univer workbook snapshot as an Excel file. */
export function exportExcelFile(snapshot: object, fileName: string): Promise<void> {
  return new Promise((resolve, reject) => {
    void LuckyExcel.transformUniverToExcel({
      snapshot,
      fileName,
      success: () => {
        resolve()
      },
      error: (err) => {
        reject(err)
      },
    })
  })
}
