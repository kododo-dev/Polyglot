import { Button, Chip, Stack, Typography } from '@mui/material'
import ArrowForwardIcon from '@mui/icons-material/ArrowForward'
import { useI18n } from '@kododo-dev/way-shell'
import type { DemoKey } from './api.ts'
import { CodeBlock, CopyField, Panel, SectionLabel } from './shared.tsx'

/** The demo's key is public, so it can be shown, unlike any real one. */
export function DemoKeyPanel({ demo }: { demo: DemoKey }) {
  const { t } = useI18n()
  return (
    <Panel title={t('tryTheApi')} aside={<Chip size="small" color="primary" variant="outlined" label={t('demoKey')} />}>
      <Stack spacing={2}>
        <Typography color="text.secondary" sx={{ fontSize: '0.85rem' }}>{t('demoKeyLead')}</Typography>
        <CopyField value={demo.token} label={t('demoKey')} testId="demo-token" />
        <div>
          <SectionLabel>{t('languages')}</SectionLabel>
          <CodeBlock>{`curl -H "X-Api-Key: ${demo.token}" "${demo.baseUrl}/api/v1/cultures"`}</CodeBlock>
        </div>
        <div>
          <SectionLabel>{t('checkoutOnly')}</SectionLabel>
          <CodeBlock>{`curl -H "X-Api-Key: ${demo.token}" "${demo.baseUrl}/api/v1/translations/${demo.culture}?namespace=Checkout"`}</CodeBlock>
        </div>
        {demo.apiReferenceUrl && (
          <div>
            <Button variant="outlined" href={demo.apiReferenceUrl} endIcon={<ArrowForwardIcon />}>{t('sendFromReference')}</Button>
          </div>
        )}
      </Stack>
    </Panel>
  )
}
