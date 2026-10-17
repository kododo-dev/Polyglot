import { useCallback, type ReactNode } from 'react'
import { Box, Button, Card, CardActionArea, Stack, Typography } from '@mui/material'
import TranslateIcon from '@mui/icons-material/Translate'
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined'
import KeyOutlinedIcon from '@mui/icons-material/KeyOutlined'
import ArrowForwardIcon from '@mui/icons-material/ArrowForward'
import { PageHeader, useI18n, useShell } from '@kododo-dev/way-shell'
import type { Overview } from '../api.ts'
import { DemoKeyPanel } from '../DemoKeyPanel.tsx'
import { Loaded, PageBody, Panel } from '../shared.tsx'
import { useLoad } from '../hooks.ts'

function Tile({ icon, label, value, note, onClick, testId }: {
  icon: ReactNode
  label: string
  value: number
  note: string
  onClick(): void
  testId: string
}) {
  return (
    <Card variant="outlined" sx={{ borderRadius: 2 }}>
      <CardActionArea onClick={onClick} sx={{ p: 2.5 }} data-testid={testId}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'text.secondary', fontSize: '0.75rem' }}>{icon}{label}</Box>
        <Typography sx={{ fontSize: '1.8rem', fontWeight: 700, lineHeight: 1.3 }}>{value}</Typography>
        <Typography noWrap sx={{ fontSize: '0.75rem', color: 'text.secondary' }}>{note}</Typography>
      </CardActionArea>
    </Card>
  )
}

export default function OverviewPage() {
  const { api, navigate } = useShell()
  const { t } = useI18n()
  const { data, error } = useLoad(useCallback(() => api.post<Overview>('Overview'), [api]))
  const openEditor = () => navigate('translations')

  return (
    <>
      <PageHeader title={t('overview')}>
        <Button variant="contained" size="small" startIcon={<TranslateIcon />} onClick={openEditor}>{t('openEditor')}</Button>
      </PageHeader>
      <PageBody>
        <Loaded data={data} error={error}>
          {(o) => (
            <>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(210px, 1fr))', gap: 2 }}>
                <Tile
                  testId="tile-languages"
                  icon={<TranslateIcon sx={{ fontSize: 16 }} />}
                  label={t('languages')}
                  value={o.cultures.length}
                  note={t('languagesNote', { cultures: o.cultures.join(', '), default: o.defaultCulture })}
                  onClick={openEditor}
                />
                <Tile
                  testId="tile-users"
                  icon={<PeopleOutlinedIcon sx={{ fontSize: 16 }} />}
                  label={t('users')}
                  value={o.userCount}
                  note={o.disabledUserCount === 0 ? t('allEnabled') : t('someDisabled', { count: o.disabledUserCount })}
                  onClick={() => navigate('users')}
                />
                <Tile
                  testId="tile-keys"
                  icon={<KeyOutlinedIcon sx={{ fontSize: 16 }} />}
                  label={t('apiKeys')}
                  value={o.activeKeyCount}
                  note={!o.apiEnabled ? t('apiOff') : o.keyCount === 0 ? t('noneYet') : t('activeOf', { count: o.keyCount })}
                  onClick={() => navigate('api-keys')}
                />
              </Box>

              <Panel title={t('gettingTranslations')}>
                <Typography color="text.secondary" sx={{ fontSize: '0.85rem', maxWidth: '70ch' }}>{t('gettingTranslationsLead')}</Typography>
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 2 }}>
                  <Button variant="outlined" startIcon={<KeyOutlinedIcon />} onClick={() => navigate('api-keys')}>{t('manageKeys')}</Button>
                  {o.apiReferenceUrl && <Button variant="outlined" href={o.apiReferenceUrl}>{t('tryInReference')}</Button>}
                  {o.openApiUrl && <Button href={o.openApiUrl} endIcon={<ArrowForwardIcon />}>{t('openApiDocument')}</Button>}
                </Stack>
              </Panel>

              {o.demoKey && <DemoKeyPanel demo={o.demoKey} />}
            </>
          )}
        </Loaded>
      </PageBody>
    </>
  )
}
