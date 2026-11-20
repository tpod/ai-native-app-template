import { useState, type FormEvent } from 'react'
import type { ActionInput, ActionName, PreviewName } from '@/api/actions'
import { PreviewConfirmDialog } from '@/components/preview-confirm-dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

type Props<N extends ActionName & PreviewName> = {
  action: N
  label: string
  placeholder?: string
  initialValue?: string
  submitLabel: string
  title: string
  /** Builds the action input from the typed text. */
  toInput: (value: string) => ActionInput<N>
  onExecuted?: (result: unknown) => void
}

/** Collects one text value, then hands it to PreviewConfirmDialog; nothing executes without a preview. */
export function InputActionForm<N extends ActionName & PreviewName>({
  action,
  label,
  placeholder,
  initialValue = '',
  submitLabel,
  title,
  toInput,
  onExecuted,
}: Props<N>) {
  const [value, setValue] = useState(initialValue)
  const id = `${action}-input`

  return (
    <form className="flex items-end gap-2" onSubmit={(e: FormEvent) => e.preventDefault()}>
      <div className="space-y-1.5">
        <Label htmlFor={id}>{label}</Label>
        <Input id={id} value={value} placeholder={placeholder} onChange={(e) => setValue(e.target.value)} />
      </div>
      <PreviewConfirmDialog
        action={action}
        input={toInput(value)}
        title={title}
        trigger={<Button type="submit">{submitLabel}</Button>}
        onExecuted={(r) => {
          setValue(initialValue)
          onExecuted?.(r)
        }}
      />
    </form>
  )
}
