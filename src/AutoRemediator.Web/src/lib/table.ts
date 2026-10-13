import { createColumnHelper, metaHelper, tableFeatures, type ColumnDef, type RowData } from '@tanstack/react-table'

export const features = tableFeatures({ columnMeta: metaHelper<{ className?: string }>() })

export type Columns<T extends RowData> = ColumnDef<typeof features, T, any>[] // eslint-disable-line @typescript-eslint/no-explicit-any

export function columnHelper<T extends RowData>() {
  return createColumnHelper<typeof features, T>()
}
