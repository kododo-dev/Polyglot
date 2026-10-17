import { useCallback, useState, type FormEvent } from 'react'
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Link, Stack, Table, TableBody, TableCell,
  TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup, Typography,
} from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import KeyOutlinedIcon from '@mui/icons-material/KeyOutlined'
import { PageHeader, useI18n, useShell } from '@kododo-dev/way-shell'
import type { ApiKey, ApiKeys, CreatedKey } from '../api.ts'
import { DemoKeyPanel } from '../DemoKeyPanel.tsx'
import { CodeBlock, CopyField, Loaded, Messages, PageBody, Panel, SectionLabel, Time } from '../shared.tsx'
import { errorMessages, useLoad } from '../hooks.ts'
import { useConfirm } from '../confirm.tsx'

/** The UTC day `days` from today, as a date input wants it. */
function inDays(days: number) {
  const day = new Date()
  day.setUTCDate(day.getUTCDate() + days)
  return day.toISOString().slice(0, 10)
}

function NewKeyDialog({ open, maxLength, onClose, onCreated }: {
  open: boolean
  maxLength: number
  onClose(): void
  onCreated(key: CreatedKey): Promise<void>
}) {
  const { api } = useShell()
  const { t } = useI18n()
  const [name, setName] = useState('')
  const [preset, setPreset] = useState<string>('')
  const [validThrough, setValidThrough] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const close = () => {
    setName('')
    setPreset('')
    setValidThrough('')
    setErrors([])
    onClose()
  }
  // The presets fill the date; picking a date by hand clears them.
  const choosePreset = (value: string | null) => {
    if (value === null) return
    setPreset(value)
    setValidThrough(value ? inDays(Number(value)) : '')
  }
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const key = await api.post<CreatedKey>('CreateKey', { name, validThrough: validThrough || null })
      close()
      await onCreated(key)
    } catch (err) {
      setErrors(errorMessages(err, t))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onClose={close} maxWidth="xs" fullWidth slotProps={{ paper: { component: 'form', onSubmit: submit } }}>
      <DialogTitle sx={{ fontSize: '0.95rem', fontWeight: 700 }}>{t('newKey')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Messages severity="error" messages={errors} />
          <TextField size="small" label={t('name')} required autoFocus autoComplete="off" placeholder="shop-frontend-prod" value={name}
            helperText={t('keyNameHint')} slotProps={{ htmlInput: { maxLength } }} onChange={(e) => setName(e.target.value)} />
          <div>
            <SectionLabel>{t('expires')}</SectionLabel>
            <ToggleButtonGroup size="small" exclusive value={preset} onChange={(_, v: string | null) => choosePreset(v)} aria-label={t('expires')} sx={{ mb: 1.5 }}>
              <ToggleButton value="30">{t('days30')}</ToggleButton>
              <ToggleButton value="90">{t('days90')}</ToggleButton>
              <ToggleButton value="365">{t('year1')}</ToggleButton>
              <ToggleButton value="">{t('never')}</ToggleButton>
            </ToggleButtonGroup>
            <TextField size="small" fullWidth type="date" label={t('validThrough')} value={validThrough} helperText={t('validThroughHint')}
              slotProps={{ inputLabel: { shrink: true } }}
              onChange={(e) => {
                setValidThrough(e.target.value)
                setPreset(e.target.value ? 'custom' : '')
              }} />
          </div>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>{t('cancel')}</Button>
        <Button type="submit" variant="contained" disabled={busy}>{t('createKey')}</Button>
      </DialogActions>
    </Dialog>
  )
}

/** The token just issued, shown until the page is left: the server keeps no copy of it. */
function CreatedKeyPanel({ created, keys }: { created: CreatedKey; keys: ApiKeys }) {
  const { t } = useI18n()
  return (
    <Panel title={t('keyCreated', { name: created.name })} aside={<Chip size="small" color="success" variant="outlined" label={t('active')} />}>
      <Stack spacing={2}>
        <Alert severity="warning"><strong>{t('copyNow')}</strong> {t('copyNowDetail')}</Alert>
        <CopyField value={created.token} label={created.name} testId="new-token" />
        <div>
          <SectionLabel>{t('tryIt')}</SectionLabel>
          <CodeBlock>{`curl -H "X-Api-Key: ${created.token}" "${keys.baseUrl}/api/v1/translations/${keys.defaultCulture}"`}</CodeBlock>
        </div>
        {keys.apiReferenceUrl && (
          <Typography color="text.secondary" sx={{ fontSize: '0.85rem' }}>
            {t('orPasteInto')} <Link href={keys.apiReferenceUrl}>{t('apiReference')}</Link>.
          </Typography>
        )}
      </Stack>
    </Panel>
  )
}

const STATUS_COLOR = { active: 'success', expired: 'warning', disabled: 'error' } as const

export default function ApiKeysPage() {
  const { api } = useShell()
  const { t } = useI18n()
  const { data, error, reload } = useLoad(useCallback(() => api.post<ApiKeys>('ApiKeys'), [api]))
  const { confirm, dialog } = useConfirm()
  const [creating, setCreating] = useState(false)
  const [created, setCreated] = useState<CreatedKey | null>(null)
  const [message, setMessage] = useState<string[]>([])
  const [errors, setErrors] = useState<string[]>([])

  const change = async (run: () => Promise<unknown>, success: string) => {
    setMessage([])
    setErrors([])
    try {
      await run()
      setMessage([success])
    } catch (e) {
      setErrors(errorMessages(e, t))
    }
    await reload()
  }

  const setDisabled = (key: ApiKey) => confirm({
    message: key.status === 'disabled' ? t('enableKeyQuestion', { name: key.name }) : t('disableKeyQuestion', { name: key.name }),
    action: key.status === 'disabled' ? t('enable') : t('disable'),
    run: () => change(
      () => api.post('SetKeyDisabled', { id: key.id, disabled: key.status !== 'disabled' }),
      key.status === 'disabled' ? t('keyEnabled') : t('keyDisabled')),
  })

  const remove = (key: ApiKey) => confirm({
    message: t('deleteKeyQuestion', { name: key.name }),
    action: t('deleteKey'),
    danger: true,
    run: () => change(() => api.post('DeleteKey', { id: key.id }), t('keyDeleted')),
  })

  const newKeyButton = <Button variant="contained" size="small" startIcon={<AddIcon />} onClick={() => setCreating(true)}>{t('newKey')}</Button>

  return (
    <>
      <PageHeader title={t('apiKeys')}>{newKeyButton}</PageHeader>
      <PageBody>
        <Loaded data={data} error={error}>
          {(k) => (
            <>
              {!k.apiEnabled && <Alert severity="warning">{t('apiTurnedOff')} <code>Polyglot__Api__Enabled=false</code></Alert>}
              {created && <CreatedKeyPanel created={created} keys={k} />}
              <Messages severity="success" messages={message} onClose={() => setMessage([])} />
              <Messages severity="error" messages={errors} onClose={() => setErrors([])} />
              {k.demoKey && <DemoKeyPanel demo={k.demoKey} />}
              <Panel padded={false}>
                {k.keys.length === 0 ? (
                  <Box sx={{ p: 5, textAlign: 'center', color: 'text.secondary' }}>
                    <KeyOutlinedIcon sx={{ fontSize: 28, color: 'text.disabled', mb: 1 }} />
                    <Typography sx={{ fontSize: '0.85rem', mb: 2 }}>{t('noKeysYet')}</Typography>
                    {newKeyButton}
                  </Box>
                ) : (
                  <Box sx={{ overflowX: 'auto' }}>
                    <Table size="small" sx={{ '& td, & th': { fontSize: '0.8rem' } }}>
                      <TableHead>
                        <TableRow>
                          <TableCell>{t('key')}</TableCell>
                          <TableCell>{t('status')}</TableCell>
                          <TableCell>{t('created')}</TableCell>
                          <TableCell>{t('lastUsed')}</TableCell>
                          <TableCell>{t('expires')}</TableCell>
                          <TableCell />
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {k.keys.map((key) => (
                          <TableRow key={key.id} hover data-testid="key-row">
                            <TableCell>
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                <Typography component="span" sx={{ fontWeight: 600, fontSize: 'inherit' }}>{key.name}</Typography>
                                {key.isDemo && <Chip size="small" color="primary" variant="outlined" label={t('demo')} />}
                              </Box>
                              <Typography component="code" sx={{ display: 'block', fontSize: '0.72rem', color: 'text.disabled' }}>{key.prefix}</Typography>
                            </TableCell>
                            <TableCell><Chip size="small" variant="outlined" color={STATUS_COLOR[key.status]} label={t(key.status)} /></TableCell>
                            <TableCell sx={{ whiteSpace: 'nowrap', color: 'text.secondary' }}><Time value={key.createdAt} /></TableCell>
                            <TableCell sx={{ whiteSpace: 'nowrap', color: 'text.secondary' }}><Time value={key.lastUsedAt} /></TableCell>
                            <TableCell sx={{ whiteSpace: 'nowrap', color: 'text.secondary' }}><Time value={key.expiresAt} /></TableCell>
                            <TableCell align="right">
                              {!key.isDemo && (
                                <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                                  <Button size="small" variant="outlined" onClick={() => setDisabled(key)}>
                                    {key.status === 'disabled' ? t('enable') : t('disable')}
                                  </Button>
                                  <Button size="small" variant="outlined" color="error" onClick={() => remove(key)}>{t('delete')}</Button>
                                </Stack>
                              )}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </Box>
                )}
              </Panel>
              <NewKeyDialog open={creating} maxLength={k.maxNameLength} onClose={() => setCreating(false)}
                onCreated={async (key) => {
                  setMessage([])
                  setErrors([])
                  setCreated(key)
                  await reload()
                }} />
            </>
          )}
        </Loaded>
      </PageBody>
      {dialog}
    </>
  )
}
