import { useState } from 'react'
import { History } from 'lucide-react'
import { HistoryPanel, type HistoryFilter } from '@/components/history-panel'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type Props = {
  /** The aggregate root's type name, e.g. `Project`. A child entity passes its root's type and id. */
  aggregateType: string
  aggregateId: string
  /** For a child entity: show only entries whose payload has this key and value, with a "show all" escape. */
  filter?: HistoryFilter
  /** What the panel title names, e.g. the project or task name. */
  label?: string
}

/** A small icon button that opens the entity's business history in a side panel. Put one on every entity a page shows. */
export function HistoryButton({ aggregateType, aggregateId, filter, label }: Props) {
  const [open, setOpen] = useState(false)
  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <Tooltip>
        <TooltipTrigger asChild>
          <SheetTrigger asChild>
            <Button variant="ghost" size="icon-sm" aria-label="Business history">
              <History />
            </Button>
          </SheetTrigger>
        </TooltipTrigger>
        <TooltipContent>Business history</TooltipContent>
      </Tooltip>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Business history</SheetTitle>
          <SheetDescription>{label ?? `${aggregateType} ${aggregateId}`}</SheetDescription>
        </SheetHeader>
        <div className="px-4 pb-4">
          <HistoryPanel aggregateType={aggregateType} aggregateId={aggregateId} filter={filter} enabled={open} />
        </div>
      </SheetContent>
    </Sheet>
  )
}
