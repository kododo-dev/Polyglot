import { useCallback, useState, type FormEvent } from 'react'
import { Alert, Box, Button, Chip, Stack, TextField, Typography } from '@mui/material'
import { PageHeader, useI18n, useShell } from '@kododo-dev/way-shell'
import type { Account } from '../api.ts'
import { Loaded, Messages, PageBody, Panel } from '../shared.tsx'
import { errorMessages, useLoad } from '../hooks.ts'

function PasswordForm({ minLength }: { minLength: number }) {
  const { api } = useShell()
  const { t } = useI18n()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [message, setMessage] = useState<string[]>([])
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setMessage([])
    setErrors([])
    setBusy(true)
    try {
      await api.post('ChangePassword', { currentPassword: current, newPassword: next })
      setCurrent('')
      setNext('')
      setMessage([t('passwordChanged')])
    } catch (err) {
      setErrors(errorMessages(err, t))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Panel title={t('changePassword')}>
      <Stack component="form" spacing={2} onSubmit={submit} sx={{ maxWidth: 380 }}>
        <Messages severity="success" messages={message} />
        <Messages severity="error" messages={errors} />
        <TextField size="small" label={t('currentPassword')} type="password" required autoComplete="current-password" value={current}
          onChange={(e) => setCurrent(e.target.value)} />
        <TextField size="small" label={t('newPassword')} type="password" required autoComplete="new-password" value={next}
          helperText={t('passwordHint', { count: minLength })} slotProps={{ htmlInput: { minLength } }} onChange={(e) => setNext(e.target.value)} />
        <Box><Button type="submit" variant="contained" disabled={busy}>{t('changePassword')}</Button></Box>
      </Stack>
    </Panel>
  )
}

export default function AccountPage() {
  const { api } = useShell()
  const { t } = useI18n()
  const { data, error } = useLoad(useCallback(() => api.post<Account>('Account'), [api]))

  return (
    <>
      <PageHeader title={t('account')} />
      <PageBody>
        <Loaded data={data} error={error}>
          {(a) => (
            <>
              {a.role === 'None' && <Alert severity="info"><strong>{t('noAccessYet')}</strong> {t('noAccessYetDetail')}</Alert>}
              <Panel
                title={a.displayName.trim() || a.username}
                aside={<Chip size="small" color="primary" variant="outlined" label={a.role === 'None' ? t('noAccess') : t(`role${a.role}`)} />}
              >
                <Typography color="text.secondary" sx={{ fontSize: '0.85rem' }}>
                  {t('signedInAs')} <code>{a.username}</code>{a.hasPassword ? '' : ` ${t('throughSso')}`}
                </Typography>
              </Panel>
              {a.isDemo ? <Alert severity="warning">{t('passwordDemo')}</Alert> : a.hasPassword && <PasswordForm minLength={a.minPasswordLength} />}
            </>
          )}
        </Loaded>
      </PageBody>
    </>
  )
}
