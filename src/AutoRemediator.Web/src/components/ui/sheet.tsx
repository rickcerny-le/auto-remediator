import * as React from 'react'
import { Dialog as DialogPrimitive } from 'radix-ui'
import { cn } from '@/lib/utils'

const Sheet = DialogPrimitive.Root

/** Left-anchored modal drawer. `title` is the accessible name (visually hidden). */
function SheetContent({
  className,
  children,
  title,
  ...props
}: Omit<React.ComponentProps<typeof DialogPrimitive.Content>, 'title'> & { title: string }) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-30 bg-black/50" />
      <DialogPrimitive.Content
        aria-describedby={undefined}
        className={cn('fixed inset-y-0 left-0 z-40 w-72 max-w-[85vw] border-r bg-sidebar py-2 shadow-lg', className)}
        {...props}
      >
        <DialogPrimitive.Title className="sr-only">{title}</DialogPrimitive.Title>
        {children}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  )
}

export { Sheet, SheetContent }
