import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { createHash } from 'node:crypto'
import { defineConfig, loadEnv, type Plugin } from 'vite'

/**
 * SASISHO16: Content-Security-Policy kwenye build ya production (meta tag).
 *  - script: faili za app tu + hash ya script ndogo ya ndani ya index.html (hakuna 'unsafe-inline'/'unsafe-eval')
 *    → hata maandishi ya mshambulizi yakifika kwenye ukurasa (XSS), hayawezi kuendesha JavaScript.
 *  - connect: API iliyotajwa kwenye VITE_API_BASE_URL tu (hakuna kutuma data kwingine).
 *  - Dev server haiguswi (HMR inahitaji inline/ws).
 * frame-ancestors HAIWEZI kuwekwa kwa meta — server ya frontend (nginx n.k.) iweke vichwa: angalia MAELEKEZO.
 */
function cspPlugin(apiBase: string): Plugin {
  let apiOrigin = ''
  try {
    apiOrigin = new URL(apiBase).origin
  } catch {
    apiOrigin = '' // anwani ya jamaa (/api) = 'self'
  }
  return {
    name: 'chamalink-csp',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler(html) {
        const hashes = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(
          (m) => `'sha256-${createHash('sha256').update(m[1]).digest('base64')}'`,
        )
        const csp = [
          "default-src 'self'",
          `script-src 'self' ${hashes.join(' ')}`.trim(),
          "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
          "font-src 'self' https://fonts.gstatic.com",
          "img-src 'self' data: blob:",
          `connect-src 'self'${apiOrigin ? ` ${apiOrigin}` : ''}`,
          "object-src 'none'",
          "base-uri 'self'",
          "form-action 'self'",
        ].join('; ')
        return html.replace('<meta charset="UTF-8" />', `<meta charset="UTF-8" />\n    <meta http-equiv="Content-Security-Policy" content="${csp}" />\n    <meta name="referrer" content="no-referrer" />`)
      },
    },
  }
}

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  return {
    plugins: [react(), tailwindcss(), cspPlugin(env.VITE_API_BASE_URL ?? 'http://localhost:5168/api')],
    build: {
      // SASISHO16: hakuna source maps kwenye production (zingeonyesha code yote ya chanzo kwa yeyote)
      sourcemap: false,
    },
    // `vite preview` ni seva ya MAJARIBIO pekee (production = nginx + dist/).
    // Bila hii, jina la host la mazingira ya majaribio linakataliwa (403 Blocked request).
    preview: { allowedHosts: true },
  }
})
