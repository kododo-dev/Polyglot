import { useState } from 'react'
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material'
import { useI18n } from '@kododo-dev/way-shell'

export interface Confirmation {
  message: string
  action: string
  danger?: boolean
  run(): Promise<void>
}

/** Asks before an action that changes or removes something; `confirm` opens it. */
export function useConfirm() {
  const { t } = useI18n()
  const [pending, setPending] = useState<Confirmation | null>(null)
  const [busy, setBusy] = useState(false)
  const close = () => {
    if (!busy) setPending(null)
  }
  const ok = async () => {
    if (!pending) return
    setBusy(true)
    try {
      await pending.run()
    } finally {
      setBusy(false)
      setPending(null)
    }
  }
  const dialog = (
    <Dialog open={!!pending} onClose={close} maxWidth="xs" fullWidth>
      <DialogTitle sx={{ fontSize: '0.95rem', fontWeight: 700 }}>{t('areYouSure')}</DialogTitle>
      <DialogContent>
        <Typography sx={{ fontSize: '0.85rem' }}>{pending?.message}</Typography>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>{t('cancel')}</Button>
        <Button variant="contained" color={pending?.danger ? 'error' : 'primary'} onClick={ok} disabled={busy}>
          {pending?.action}
        </Button>
      </DialogActions>
    </Dialog>
  )
  return { confirm: setPending, dialog }
}
