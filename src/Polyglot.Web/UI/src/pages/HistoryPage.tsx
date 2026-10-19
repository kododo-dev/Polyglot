import { useCallback, useState, type FormEvent } from 'react'
import {
  Box, Button, Chip, Link, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import HistoryIcon from '@mui/icons-material/History'
import { useLocation } from 'react-router-dom'
import { PageHeader, useI18n, useShell } from '@kododo-dev/way-shell'
import type { Change, History, HistoryQuery } from '../api.ts'
import { Loaded, Messages, PageBody, Panel, Time } from '../shared.tsx'
import { errorMessages, useLoad } from '../hooks.ts'
import { useConfirm } from '../confirm.tsx'

const short = (value: string) => (value.length > 80 ? `${value.slice(0, 80)}…` : value)

/** The filters live in the address (`#/history/?key=...`), so a filtered view can be shared and survives a reload. */
function useQuery(): [HistoryQuery, (query: HistoryQuery) => void] {
  const { navigate, wayId } = useShell()
  const params = new URLSearchParams(useLocation().search)
  const query: HistoryQuery = {
    key: params.get('key') ?? undefined,
    culture: params.get('culture') ?? undefined,
    userId: params.get('user') ?? undefined,
    from: params.get('from') ?? undefined,
    to: params.get('to') ?? undefined,
    before: params.get('before') ? Number(params.get('before')) : undefined,
  }
  const set = (next: HistoryQuery) => {
    const search = new URLSearchParams()
    if (next.key) search.set('key', next.key)
    if (next.culture) search.set('culture', next.culture)
    if (next.userId) search.set('user', next.userId)
    if (next.from) search.set('from', next.from)
    if (next.to) search.set('to', next.to)
    if (next.before) search.set('before', String(next.before))
    const text = search.toString()
    navigate(wayId, text ? `?${text}` : '')
  }
  return [query, set]
}

function ChangeCell({ change }: { change: Change }) {
  const { t } = useI18n()
  const value = { display: 'block', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' } as const
  const removed = <Box component="del" sx={{ ...value, color: 'error.main', bgcolor: 'rgba(211, 47, 47, 0.08)' }}>{change.oldValue}</Box>
  const added = <Box component="span" sx={{ ...value, color: 'success.main', bgcolor: 'rgba(46, 125, 50, 0.08)' }}>{change.newValue}</Box>
  if (change.oldValue === null)
    return <><Chip size="small" color="success" variant="outlined" label={t('added')} sx={{ mb: 0.5 }} />{added}</>
  if (change.newValue === null)
    return <><Chip size="small" color="error" variant="outlined" label={t('removed')} sx={{ mb: 0.5 }} />{removed}</>
  return <>{removed}{added}</>
}

export default function HistoryPage() {
  const { api } = useShell()
  const { t } = useI18n()
  const [query, setQuery] = useQuery()
  const queryKey = JSON.stringify(query)
  // Reloads when the query in the address changes.
  const load = useCallback(() => api.post<History>('History', JSON.parse(queryKey) as HistoryQuery), [api, queryKey])
  const { data, error, reload } = useLoad(load)
  const { confirm, dialog } = useConfirm()
  const [message, setMessage] = useState<string[]>([])
  const [errors, setErrors] = useState<string[]>([])
  const [draft, setDraft] = useState<HistoryQuery>(query)
  const [draftFor, setDraftFor] = useState(queryKey)
  // The fields follow the address when it changes from outside the form (a key link, Clear, the back button).
  if (draftFor !== queryKey) {
    setDraftFor(queryKey)
    setDraft(query)
  }

  const filtered = !!(query.key || query.culture || query.userId || query.from || query.to)
  const submit = (e: FormEvent) => {
    e.preventDefault()
    setQuery({ ...draft, before: undefined })
  }

  const undo = (change: Change) => confirm({
    message: change.oldValue === null
      ? t('undoAddedQuestion', { key: change.key, culture: change.culture })
      : t('undoQuestion', { key: change.key, culture: change.culture, value: short(change.oldValue) }),
    action: t('undo'),
    run: async () => {
      setMessage([])
      setErrors([])
      try {
        await api.post('Undo', { id: change.id })
        setMessage([t('undone')])
        await reload()
      } catch (e) {
        setErrors(errorMessages(e, t))
      }
    },
  })

  const field = { size: 'small', slotProps: { inputLabel: { shrink: true } } } as const
  // Shows "All" / "Anyone" when nothing is chosen; a select hides its empty option otherwise.
  const selectField = { size: 'small', slotProps: { inputLabel: { shrink: true }, select: { displayEmpty: true } } } as const

  return (
    <>
      <PageHeader title={t('history')} />
      <PageBody>
        <Messages severity="success" messages={message} onClose={() => setMessage([])} />
        <Messages severity="error" messages={errors} onClose={() => setErrors([])} />
        <Loaded data={data} error={error}>
          {(h) => (
            <>
              <Box component="form" role="search" onSubmit={submit} sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5, alignItems: 'flex-end' }}>
                <TextField {...field} label={t('key')} placeholder={t('partOfAKey')} value={draft.key ?? ''} autoComplete="off"
                  onChange={(e) => setDraft({ ...draft, key: e.target.value })} sx={{ flex: '2 1 200px' }} />
                <TextField {...selectField} select label={t('language')} value={draft.culture ?? ''}
                  onChange={(e) => setDraft({ ...draft, culture: e.target.value })} sx={{ flex: '1 1 130px' }}>
                  <MenuItem value="">{t('all')}</MenuItem>
                  {h.cultures.map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
                </TextField>
                <TextField {...selectField} select label={t('changedBy')} value={draft.userId ?? ''}
                  onChange={(e) => setDraft({ ...draft, userId: e.target.value })} sx={{ flex: '1 1 130px' }}>
                  <MenuItem value="">{t('anyone')}</MenuItem>
                  {h.users.map((u) => <MenuItem key={u.id} value={u.id}>{u.name}</MenuItem>)}
                </TextField>
                <TextField {...field} type="date" label={t('fromUtc')} value={draft.from ?? ''}
                  onChange={(e) => setDraft({ ...draft, from: e.target.value })} sx={{ flex: '1 1 130px' }} />
                <TextField {...field} type="date" label={t('toUtc')} value={draft.to ?? ''}
                  onChange={(e) => setDraft({ ...draft, to: e.target.value })} sx={{ flex: '1 1 130px' }} />
                <Stack direction="row" spacing={1}>
                  <Button type="submit" variant="contained">{t('filter')}</Button>
                  {filtered && <Button onClick={() => setQuery({})}>{t('clear')}</Button>}
                </Stack>
              </Box>

              <Panel padded={false}>
                {h.changes.length === 0 ? (
                  <Box sx={{ p: 5, textAlign: 'center', color: 'text.secondary' }}>
                    <HistoryIcon sx={{ fontSize: 28, color: 'text.disabled', mb: 1 }} />
                    <Typography sx={{ fontSize: '0.85rem' }}>{filtered ? t('noMatchingChanges') : t('noChangesYet')}</Typography>
                  </Box>
                ) : (
                  <Box sx={{ overflowX: 'auto' }}>
                    <Table size="small" sx={{ '& td, & th': { fontSize: '0.8rem' } }}>
                      <TableHead>
                        <TableRow>
                          <TableCell>{t('translation')}</TableCell>
                          <TableCell>{t('change')}</TableCell>
                          <TableCell>{t('by')}</TableCell>
                          <TableCell>{t('when')}</TableCell>
                          <TableCell><Box component="span" sx={visuallyHidden}>{t('actions')}</Box></TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {h.changes.map((c) => (
                          <TableRow key={c.id} hover data-testid="change">
                            <TableCell>
                              <Link component="button" type="button" underline="hover" color="inherit" sx={{ fontWeight: 600, textAlign: 'left' }}
                                onClick={() => setQuery({ key: c.key })}>{c.key}</Link>
                              <Typography sx={{ fontSize: '0.72rem', color: 'text.disabled' }}>{c.culture}</Typography>
                            </TableCell>
                            <TableCell sx={{ maxWidth: 420 }}><ChangeCell change={c} /></TableCell>
                            <TableCell>{c.userName}</TableCell>
                            <TableCell sx={{ whiteSpace: 'nowrap', color: 'text.secondary' }}><Time value={c.changedAt} /></TableCell>
                            <TableCell align="right"><Button size="small" variant="outlined" onClick={() => undo(c)}>{t('undo')}</Button></TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </Box>
                )}
              </Panel>

              {(query.before !== undefined || h.olderThan !== null) && (
                <Stack direction="row" spacing={1} sx={{ justifyContent: 'center' }}>
                  {query.before !== undefined && <Button variant="outlined" onClick={() => setQuery({ ...query, before: undefined })}>{t('newest')}</Button>}
                  {h.olderThan !== null && <Button variant="outlined" onClick={() => setQuery({ ...query, before: h.olderThan! })}>{t('olderChanges')}</Button>}
                </Stack>
              )}
            </>
          )}
        </Loaded>
      </PageBody>
      {dialog}
    </>
  )
}

const visuallyHidden = { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clipPath: 'inset(50%)', whiteSpace: 'nowrap' } as const
