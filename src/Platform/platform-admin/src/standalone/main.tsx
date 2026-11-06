import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource-variable/inter'
import '@fontsource-variable/plus-jakarta-sans'
import '../index.css'
import { mappingName } from '../lib/brand'
import { startTheme } from '../lib/theme'
import StandaloneDesignerApp from './StandaloneDesignerApp'

/** Entry of the standalone mapping designer page (mapping-designer.html, vite.mapping.config.ts). */
document.title = mappingName

startTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StandaloneDesignerApp />
  </StrictMode>,
)
