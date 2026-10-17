import { Alert } from '@mui/material'
import type { WayProps } from '@kododo-dev/way-shell'
import OverviewPage from './pages/OverviewPage.tsx'
import HistoryPage from './pages/HistoryPage.tsx'
import UsersPage from './pages/UsersPage.tsx'
import ApiKeysPage from './pages/ApiKeysPage.tsx'
import AccountPage from './pages/AccountPage.tsx'

/** One module, mounted as several Ways: the Way's id picks the page. */
export default function App({ wayId }: WayProps) {
  switch (wayId) {
    case 'overview': return <OverviewPage />
    case 'history': return <HistoryPage />
    case 'users': return <UsersPage />
    case 'api-keys': return <ApiKeysPage />
    case 'account': return <AccountPage />
    default: return <Alert severity="warning" sx={{ m: 3 }}>Polyglot has no page “{wayId}”.</Alert>
  }
}
