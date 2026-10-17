import { useState, type ReactNode } from 'react'
import { Alert, Box, CircularProgress, IconButton, Paper, Stack, Tooltip, Typography, type Theme } from '@mui/material'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import CheckIcon from '@mui/icons-material/Check'
import { useI18n } from '@kododo-dev/way-shell'
import { errorMessages } from './hooks.ts'

/** The Way theme's font: plain elements such as code would inherit the shell's from the page body. */
const themeFont = (theme: Theme) => theme.typography.fontFamily

/** The page under the PageHeader: centred and as wide as the old pages, scrolling with the shell. */
export function PageBody({ children }: { children: ReactNode }) {
  return (
    <Box sx={{ p: { xs: 2, md: 3 } }}>
      <Stack spacing={2.5} sx={{ maxWidth: 980, mx: 'auto' }}>{children}</Stack>
    </Box>
  )
}

/** The data of a page, a spinner until it is there, or what went wrong. */
export function Loaded<T>({ data, error, children }: { data: T | undefined; error: unknown; children(data: T): ReactNode }) {
  const { t } = useI18n()
  if (data !== undefined) return <>{children(data)}</>
  if (error !== undefined) return <Messages severity="error" messages={errorMessages(error, t)} />
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }} aria-label={t('loading')}>
      <CircularProgress size={28} />
    </Box>
  )
}

export function Messages({ severity, messages, onClose }: {
  severity: 'success' | 'error' | 'warning' | 'info'
  messages: string[]
  onClose?(): void
}) {
  if (messages.length === 0) return null
  return (
    <Alert severity={severity} onClose={onClose} role={severity === 'error' ? 'alert' : 'status'}>
      {messages.map((m) => <div key={m}>{m}</div>)}
    </Alert>
  )
}

/** A bordered block of the page with an optional heading row. */
export function Panel({ title, aside, children, padded = true }: {
  title?: ReactNode
  aside?: ReactNode
  children: ReactNode
  padded?: boolean
}) {
  return (
    <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
      {title && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 2.5, py: 1.75, borderBottom: 1, borderColor: 'divider' }}>
          <Typography component="h2" sx={{ flex: 1, fontSize: '0.9rem', fontWeight: 700 }}>{title}</Typography>
          {aside}
        </Box>
      )}
      {padded ? <Box sx={{ p: 2.5 }}>{children}</Box> : children}
    </Paper>
  )
}

/** A short uppercase label above a block. */
export function SectionLabel({ children }: { children: ReactNode }) {
  return (
    <Typography sx={{ fontSize: '0.68rem', letterSpacing: '0.08em', textTransform: 'uppercase', color: 'text.secondary', mb: 1 }}>
      {children}
    </Typography>
  )
}

/** Text to copy, such as a token, with a copy button. */
export function CopyField({ value, label, testId }: { value: string; label: string; testId?: string }) {
  const { t } = useI18n()
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // clipboard blocked; the text stays selectable
    }
  }
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1.5, py: 1, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'action.hover' }}>
      <Typography component="code" data-testid={testId} sx={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere', fontSize: '0.82rem' }}>
        {value}
      </Typography>
      <Tooltip title={copied ? t('copied') : t('copy')}>
        <IconButton size="small" onClick={copy} aria-label={`${t('copy')}: ${label}`}>
          {copied ? <CheckIcon fontSize="small" /> : <ContentCopyIcon fontSize="small" />}
        </IconButton>
      </Tooltip>
    </Box>
  )
}

/** A command to run, such as a curl call. */
export function CodeBlock({ children }: { children: string }) {
  return (
    <Box
      component="pre"
      sx={{ m: 0, p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'action.hover', fontSize: '0.78rem', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', fontFamily: themeFont }}
    >
      {children}
    </Box>
  )
}

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 31536000], ['month', 2592000], ['week', 604800], ['day', 86400], ['hour', 3600], ['minute', 60],
]

/** A point in time as "3 days ago", the exact time on hover; a dash when there is none. */
export function Time({ value }: { value: string | null | undefined }) {
  const { t, language } = useI18n()
  // Relative to when the row was rendered first, so a re-render does not change the text under the pointer.
  const [now] = useState(Date.now)
  if (!value) return <span>—</span>
  const at = new Date(value)
  const seconds = (at.getTime() - now) / 1000
  const [unit, size] = UNITS.find(([, s]) => Math.abs(seconds) >= s) ?? ['second', 1]
  const text = Math.abs(seconds) < 60 ? t('justNow') : new Intl.RelativeTimeFormat(language, { numeric: 'auto' }).format(Math.round(seconds / size), unit)
  return (
    <Tooltip title={at.toLocaleString(language)}>
      <time dateTime={value}>{text}</time>
    </Tooltip>
  )
}
