"use client"

import * as React from "react"
import { cn, focusRing } from "@/lib/utils"
import { Switch as SwitchPrimitive } from "radix-ui"

function Switch({
  className,
  size = "default",
  ...props
}: React.ComponentProps<typeof SwitchPrimitive.Root> & {
  size?: "sm" | "default"
}) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      data-size={size}
      className={cn(
        focusRing,
        "peer group/switch inline-flex shrink-0 items-center rounded-full border shadow-xs transition-all disabled:cursor-not-allowed disabled:opacity-60 data-[size=default]:h-6 data-[size=default]:w-10 data-[size=sm]:h-6 data-[size=sm]:w-10 data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=unchecked]:border-input data-[state=unchecked]:bg-muted",
        className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className={cn(
          "pointer-events-none block rounded-full bg-background ring-0 transition-transform group-data-[size=default]/switch:size-4 group-data-[size=sm]/switch:size-4 data-[state=checked]:translate-x-[calc(100%+4px)] data-[state=unchecked]:translate-x-0.5 data-[state=checked]:bg-primary-foreground data-[state=unchecked]:bg-foreground"
        )}
      />
    </SwitchPrimitive.Root>
  )
}

export { Switch }
