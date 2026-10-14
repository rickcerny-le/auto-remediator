import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from '@/components/ui/accordion'
import { cn } from '@/lib/utils'

export interface FileComparisonItem {
  /** Unique within the list; also the accordion item value. */
  key: string
  /** Trigger text. Conveys the kind of change in words, never by colour alone. */
  label: string
  /** Text shown in the "Before" block, already resolved (use a placeholder for a missing side). */
  before: string
  /** Text shown in the "After" block, already resolved. */
  after: string
  /** Optional visible note shown under both blocks, for example that the content was truncated. */
  note?: string
}

/**
 * Before/after accordion shared by the held-review proposal and the read-only pull request diff.
 * Every item starts open. Long lines wrap (`whitespace-pre-wrap`) so the blocks do not need to scroll.
 */
export function FileComparison({
  items,
  itemTestId,
  breakLongLines = false,
}: {
  items: FileComparisonItem[]
  itemTestId: string
  /** Also break unbroken runs of characters, so a block can never scroll horizontally. */
  breakLongLines?: boolean
}) {
  const pre = cn('overflow-x-auto rounded bg-muted p-2 font-mono text-label whitespace-pre-wrap', breakLongLines && 'break-all')
  return (
    <Accordion type="multiple" defaultValue={items.map((i) => i.key)} className="rounded-md border px-4">
      {items.map((item) => (
        <AccordionItem key={item.key} value={item.key} data-testid={itemTestId}>
          <AccordionTrigger className="font-mono">{item.label}</AccordionTrigger>
          <AccordionContent className="flex flex-wrap gap-3">
            <div className="min-w-[280px] flex-1">
              <div className="text-label text-muted-foreground">Before</div>
              <pre className={pre}>{item.before}</pre>
            </div>
            <div className="min-w-[280px] flex-1">
              <div className="text-label text-muted-foreground">After</div>
              <pre className={pre}>{item.after}</pre>
            </div>
            {item.note && <p className="w-full text-label text-muted-foreground">{item.note}</p>}
          </AccordionContent>
        </AccordionItem>
      ))}
    </Accordion>
  )
}
