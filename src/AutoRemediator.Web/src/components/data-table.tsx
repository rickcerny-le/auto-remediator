import type { MouseEvent, ReactNode } from 'react'
import { Link } from 'react-router'
import { useTable, type RowData } from '@tanstack/react-table'
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { features, type Columns } from '@/lib/table'
import { cn, focusRing } from '@/lib/utils'

interface DataTableProps<T extends RowData> {
  columns: Columns<T>
  data: T[]
  /** Accessible name of the table, rendered as a visually hidden caption. */
  caption: string
  empty?: ReactNode
  loading?: boolean
  /** Where a row leads. The primary cell becomes a link to it (the only tab stop in the row). */
  rowHref?: (row: T) => string
  /** Column holding the row link; defaults to the first column. */
  primaryColumnId?: string
  testId?: string
  className?: string
}

/** A read-only TanStack table rendered with the shadcn table primitives. */
export function DataTable<T extends RowData>({
  columns,
  data,
  caption,
  empty,
  loading,
  rowHref,
  primaryColumnId,
  testId,
  className,
}: DataTableProps<T>) {
  const table = useTable({ features, columns, data })
  const rows = table.getRowModel().rows
  const primaryId = primaryColumnId ?? table.getAllLeafColumns()[0]?.id

  // Mouse convenience only: clicking the row delegates to its link, so modifier keys, middle click
  // and history behave as for a real link. Clicks on other links/buttons and text selections are ignored.
  const clickRow = (e: MouseEvent<HTMLTableRowElement>) => {
    if ((e.target as HTMLElement).closest('a, button')) return
    if (window.getSelection()?.toString()) return
    e.currentTarget.querySelector<HTMLAnchorElement>('a[data-row-link]')?.click()
  }

  return (
    <Table data-testid={testId} className={className} containerLabel={caption}>
      <TableCaption className="sr-only">{caption}</TableCaption>
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
            <TableRow key={row.id} onClick={rowHref ? clickRow : undefined} className={cn(rowHref && 'cursor-pointer')}>
              {row.getAllCells().map((cell) => {
                const content = <table.FlexRender cell={cell} />
                return (
                  <TableCell key={cell.id} className={cell.column.columnDef.meta?.className}>
                    {rowHref && cell.column.id === primaryId ? (
                      <Link
                        to={rowHref(row.original)}
                        data-row-link=""
                        className={cn(focusRing, 'rounded-sm hover:underline')}
                      >
                        {content}
                      </Link>
                    ) : (
                      content
                    )}
                  </TableCell>
                )
              })}
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  )
}
