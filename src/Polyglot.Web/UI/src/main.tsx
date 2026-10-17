// `npm run dev`: one of Polyglot's pages inside WayDevHost (?way=history), with Polyglot's API through Vite's proxy.
// Sign in to Polyglot on the proxied origin first; the dev server shares its cookie only on the same host.
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { WayDevHost } from '@kododo-dev/way-shell'
import way from './way.tsx'

const id = new URLSearchParams(window.location.search).get('way') ?? 'overview'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <WayDevHost way={way} id={id} name={id} apiBase={`/${id}/api`} title="Polyglot" />
  </StrictMode>,
)
