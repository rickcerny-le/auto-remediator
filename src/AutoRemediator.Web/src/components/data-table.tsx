import type { ReactNode } from 'react'
import { useTable, type RowData } from '@tanstack/react-table'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { features, type Columns } from '@/lib/table'
import { cn } from '@/lib/utils'

interface DataTableProps<T extends RowData> {
  columns: Columns<T>
  data: T[]
  empty?: ReactNode
  loading?: boolean
  onRowClick?: (row: T) => void
  testId?: string
  className?: string
}

/** A read-only TanStack table rendered with the shadcn table primitives. */
export function DataTable<T extends RowData>({ columns, data, empty, loading, onRowClick, testId, className }: DataTableProps<T>) {
  const table = useTable({ features, columns, data })
  const rows = table.getRowModel().rows

  return (
    <Table data-testid={testId} className={className}>
      <TableHeader>
        {table.getHeaderGroups().map((group) => (
          <TableRow key={group.id}>
            {group.headers.map((header) => (
              <TableHead key={header.id} className={header.column.columnDef.meta?.className}>
                {header.isPlaceholder ? null : <table.FlexRender header={header} />}
              </TableHead>
            ))}
          </TableRow>
        ))}
      </TableHeader>
      <TableBody>
        {rows.length === 0 ? (
          <TableRow>
            <TableCell colSpan={columns.length} className="py-6 text-muted-foreground">
              {loading ? 'Loading…' : empty}
            </TableCell>
          </TableRow>
        ) : (
          rows.map((row) => (
            <TableRow
              key={row.id}
              onClick={onRowClick && (() => onRowClick(row.original))}
              className={cn(onRowClick && 'cursor-pointer')}
            >
              {row.getAllCells().map((cell) => (
                <TableCell key={cell.id} className={cell.column.columnDef.meta?.className}>
                  <table.FlexRender cell={cell} />
                </TableCell>
              ))}
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  )
}
