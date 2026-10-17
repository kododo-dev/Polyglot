import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { wayModule } from '@kododo-dev/way-shell/vite'

// `npm run build`: src/way.tsx as a Way.Shell module (dist/way.js, dist/way.css), embedded in Polyglot.Web and mounted
// once per page (overview, history, users, API keys, account).
// `npm run dev`: src/main.tsx with one page in WayDevHost (?way=history); its API goes to Polyglot (dotnet run).
const backend = process.env.POLYGLOT_BACKEND ?? 'http://localhost:5097'
export default defineConfig({
  plugins: [react(), wayModule()],
  server: {
    proxy: Object.fromEntries(['overview', 'history', 'users', 'api-keys', 'account'].map((way) => [`/${way}/api`, backend])),
  },
})
