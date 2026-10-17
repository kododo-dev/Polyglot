import type { ThemeOptions } from '@mui/material'
import { defineWay } from '@kododo-dev/way-shell'
import App from './App.tsx'
import { translations } from './i18n.ts'

// IBM Plex Mono when installed, otherwise the system's monospace font: the editor's type, so every page of the panel
// looks like the same application.
const MONO_FONT = "'IBM Plex Mono', ui-monospace, 'Cascadia Code', 'SF Mono', Menlo, Consolas, 'Liberation Mono', monospace"

/** The CultureWay editor's look on top of the shell's theme. */
function theme(mode: 'light' | 'dark'): ThemeOptions {
  return {
    palette: mode === 'dark'
      ? { background: { default: '#1a1a1a', paper: '#222' } }
      : { background: { default: '#f5f5f5', paper: '#fafafa' } },
    typography: { fontFamily: MONO_FONT },
  }
}

// The module Way.Shell loads (built by `npm run build` into dist/way.js), once for each of Polyglot's pages.
export default defineWay({ App, translations, theme })
