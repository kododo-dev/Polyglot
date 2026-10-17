import { useCallback, useState, type FormEvent } from 'react'
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Radio,
  RadioGroup, Select, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import { PageHeader, useI18n, useShell } from '@kododo-dev/way-shell'
import type { User, UserRole, Users } from '../api.ts'
import { Loaded, Messages, PageBody, Panel } from '../shared.tsx'
import { errorMessages, useLoad } from '../hooks.ts'
import { useConfirm } from '../confirm.tsx'

const ROLES: UserRole[] = ['None', 'Editor', 'Admin']

function useRoleName() {
  const { t } = useI18n()
  return (role: UserRole) => (role === 'None' ? t('noAccess') : t(`role${role}`))
}

function AddUserDialog({ open, minLength, onClose, onCreated }: {
  open: boolean
  minLength: number
  onClose(): void
  onCreated(username: string): Promise<void>
}) {
  const { api } = useShell()
  const { t } = useI18n()
  const empty = { username: '', displayName: '', password: '', role: 'Editor' as UserRole }
  const [input, setInput] = useState(empty)
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const close = () => {
    setInput(empty)
    setErrors([])
    onClose()
  }
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      await api.post('CreateUser', input)
      const username = input.username.trim()
      close()
      await onCreated(username)
    } catch (err) {
      setErrors(errorMessages(err, t))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onClose={close} maxWidth="xs" fullWidth slotProps={{ paper: { component: 'form', onSubmit: submit } }}>
      <DialogTitle sx={{ fontSize: '0.95rem', fontWeight: 700 }}>{t('addUser')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Messages severity="error" messages={errors} />
          <TextField size="small" label={t('username')} required autoFocus autoComplete="off" value={input.username}
            onChange={(e) => setInput({ ...input, username: e.target.value })} />
          <TextField size="small" label={t('displayName')} autoComplete="off" value={input.displayName} helperText={t('displayNameHint')}
            onChange={(e) => setInput({ ...input, displayName: e.target.value })} />
          <TextField size="small" label={t('password')} type="password" required autoComplete="new-password" value={input.password}
            helperText={t('newUserPasswordHint', { count: minLength })} slotProps={{ htmlInput: { minLength } }}
            onChange={(e) => setInput({ ...input, password: e.target.value })} />
          <RadioGroup row aria-label={t('role')} value={input.role} onChange={(e) => setInput({ ...input, role: e.target.value as UserRole })}>
            <FormControlLabel value="Editor" control={<Radio size="small" />} label={t('roleEditor')} />
            <FormControlLabel value="Admin" control={<Radio size="small" />} label={t('roleAdmin')} />
          </RadioGroup>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>{t('cancel')}</Button>
        <Button type="submit" variant="contained" disabled={busy}>{t('createUser')}</Button>
      </DialogActions>
    </Dialog>
  )
}

function ResetPasswordDialog({ user, minLength, onClose, onDone }: {
  user: User | null
  minLength: number
  onClose(): void
  onDone(): void
}) {
  const { api } = useShell()
  const { t } = useI18n()
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [busy, setBusy] = useState(false)

  const close = () => {
    setPassword('')
    setErrors([])
    onClose()
  }
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!user) return
    setBusy(true)
    try {
      await api.post('ResetPassword', { id: user.id, newPassword: password })
      close()
      onDone()
    } catch (err) {
      setErrors(errorMessages(err, t))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={!!user} onClose={close} maxWidth="xs" fullWidth slotProps={{ paper: { component: 'form', onSubmit: submit } }}>
      <DialogTitle sx={{ fontSize: '0.95rem', fontWeight: 700 }}>{t('resetPassword')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography sx={{ fontSize: '0.85rem', color: 'text.secondary' }}>{t('resetPasswordLead', { username: user?.username ?? '' })}</Typography>
          <Messages severity="error" messages={errors} />
          <TextField size="small" label={t('newPassword')} type="password" required autoFocus autoComplete="new-password" value={password}
            helperText={t('passwordHint', { count: minLength })} slotProps={{ htmlInput: { minLength } }}
            onChange={(e) => setPassword(e.target.value)} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>{t('cancel')}</Button>
        <Button type="submit" variant="contained" disabled={busy}>{t('resetPassword')}</Button>
      </DialogActions>
    </Dialog>
  )
}

export default function UsersPage() {
  const { api } = useShell()
  const { t } = useI18n()
  const roleName = useRoleName()
  const { data, error, reload } = useLoad(useCallback(() => api.post<Users>('Users'), [api]))
  const { confirm, dialog } = useConfirm()
  const [adding, setAdding] = useState(false)
  const [resetting, setResetting] = useState<User | null>(null)
  const [message, setMessage] = useState<string[]>([])
  const [errors, setErrors] = useState<string[]>([])

  /** Runs a change, then shows its outcome and the users as they are now. */
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

  const setRole = (user: User, role: UserRole) => confirm({
    message: t('changeRoleQuestion', { username: user.username, role: roleName(role) }),
    action: t('changeRole'),
    run: () => change(() => api.post('SetRole', { id: user.id, role }), t('roleUpdated')),
  })

  const setDisabled = (user: User) => confirm({
    message: user.isDisabled ? t('enableUserQuestion', { username: user.username }) : t('disableUserQuestion', { username: user.username }),
    action: user.isDisabled ? t('enable') : t('disable'),
    danger: !user.isDisabled,
    run: () => change(
      () => api.post('SetDisabled', { id: user.id, disabled: !user.isDisabled }),
      user.isDisabled ? t('userEnabled') : t('userDisabled')),
  })

  return (
    <>
      <PageHeader title={t('users')}>
        {data && !data.readOnly && (
          <Button variant="contained" size="small" startIcon={<AddIcon />} onClick={() => setAdding(true)}>{t('addUser')}</Button>
        )}
      </PageHeader>
      <PageBody>
        <Typography color="text.secondary" sx={{ fontSize: '0.85rem' }}>{t('usersLead')}</Typography>
        <Messages severity="success" messages={message} onClose={() => setMessage([])} />
        <Messages severity="error" messages={errors} onClose={() => setErrors([])} />
        <Loaded data={data} error={error}>
          {(u) => (
            <>
              {u.readOnly && <Alert severity="warning">{t('usersReadOnly')}</Alert>}
              <Panel padded={false}>
                <Box sx={{ overflowX: 'auto' }}>
                  <Table size="small" sx={{ '& td, & th': { fontSize: '0.8rem' } }}>
                    <TableHead>
                      <TableRow>
                        <TableCell>{t('user')}</TableCell>
                        <TableCell>{t('role')}</TableCell>
                        <TableCell>{t('status')}</TableCell>
                        <TableCell />
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {u.users.map((user) => {
                        const isSelf = user.id === u.currentUserId
                        // Their own row and, in the demo, every row show the state without the controls.
                        const locked = isSelf || u.readOnly
                        const hasDisplayName = !!user.displayName.trim() && user.displayName !== user.username
                        const details = [hasDisplayName ? user.username : null, user.isExternal ? t('singleSignOn') : null].filter(Boolean)
                        return (
                          <TableRow key={user.id} hover data-testid="user-row">
                            <TableCell>
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                <Typography component="span" sx={{ fontWeight: 600, fontSize: 'inherit' }}>{hasDisplayName ? user.displayName : user.username}</Typography>
                                {isSelf && <Chip size="small" color="primary" variant="outlined" label={t('you')} />}
                              </Box>
                              {details.length > 0 && <Typography sx={{ fontSize: '0.72rem', color: 'text.disabled' }}>{details.join(' · ')}</Typography>}
                            </TableCell>
                            <TableCell>
                              {locked ? roleName(user.role) : (
                                <Select size="small" value={user.role} onChange={(e) => setRole(user, e.target.value as UserRole)}
                                  inputProps={{ 'aria-label': t('roleOf', { username: user.username }) }} sx={{ fontSize: '0.78rem' }}>
                                  {ROLES.map((r) => <MenuItem key={r} value={r}>{roleName(r)}</MenuItem>)}
                                </Select>
                              )}
                            </TableCell>
                            <TableCell>
                              {user.isDisabled ? <Chip size="small" color="error" variant="outlined" label={t('disabled')} />
                                : user.role === 'None' ? <Chip size="small" color="warning" variant="outlined" label={t('noAccessStatus')} />
                                  : <Chip size="small" color="success" variant="outlined" label={t('active')} />}
                            </TableCell>
                            <TableCell align="right">
                              <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                                {user.hasPassword && !u.readOnly && (
                                  <Button size="small" variant="outlined" onClick={() => setResetting(user)}>{t('resetPassword')}</Button>
                                )}
                                {!locked && (
                                  <Button size="small" variant="outlined" color={user.isDisabled ? 'primary' : 'error'} onClick={() => setDisabled(user)}>
                                    {user.isDisabled ? t('enable') : t('disable')}
                                  </Button>
                                )}
                              </Stack>
                            </TableCell>
                          </TableRow>
                        )
                      })}
                    </TableBody>
                  </Table>
                </Box>
              </Panel>
              <AddUserDialog open={adding} minLength={u.minPasswordLength} onClose={() => setAdding(false)}
                onCreated={(username) => change(async () => {}, t('userCreated', { username }))} />
              <ResetPasswordDialog user={resetting} minLength={u.minPasswordLength} onClose={() => setResetting(null)}
                onDone={() => setMessage([t('passwordReset')])} />
            </>
          )}
        </Loaded>
      </PageBody>
      {dialog}
    </>
  )
}
