import fs from 'node:fs'
import path from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

/**
 * The standalone mapping designer: the same designer as the Studio's Mapping tab (src/mapping), built as its
 * own small page for the designer API's standalone mode and its image. No sign-in, no Studio shell.
 *
 *   npm run build:mapping   -> dist-mapping/ (copied into the designer API's wwwroot by its Dockerfile)
 *   npm run dev:mapping     -> http://localhost:5187, /api proxied to a designer API on :5215
 */

/** The page is mapping-designer.html in the source tree (beside the Studio's index.html) but index.html in
 *  the output, which is what the API serves at /. Dev serves it at / too. */
function servedAsIndex(): Plugin {
  return {
    name: 'mapping-designer-as-index',
    configureServer(server) {
      server.middlewares.use((req, _res, next) => {
        if (req.url === '/' || req.url === '/index.html') req.url = '/mapping-designer.html'
        next()
      })
    },
    // After writing, not in generateBundle: Vite emits the HTML page outside the bundle a plugin can rename.
    writeBundle(options) {
      const dir = options.dir ?? 'dist-mapping'
      const page = path.join(dir, 'mapping-designer.html')
      if (fs.existsSync(page)) fs.renameSync(page, path.join(dir, 'index.html'))
    },
  }
}

export default defineConfig({
  plugins: [react(), tailwindcss(), servedAsIndex()],
  // Same origin as the API that serves the page: every engine call is a relative /api/... (lib/ufmappingApi).
  define: { 'import.meta.env.VITE_UFMAPPING_API_BASE_URL': JSON.stringify('') },
  build: {
    outDir: 'dist-mapping',
    emptyOutDir: true,
    rollupOptions: { input: 'mapping-designer.html' },
  },
  server: {
    port: 5187,
    strictPort: true,
    proxy: { '/api': process.env.MAPPING_API_URL ?? 'http://localhost:5215' },
  },
})
