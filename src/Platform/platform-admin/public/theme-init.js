// Applies the studio's light/dark choice before the first paint (src/lib/theme.ts owns it after that).
(function () {
  var saved = null
  try { saved = localStorage.getItem('studio-theme') } catch (e) { /* storage refused */ }
  var dark = saved === 'dark' || (saved !== 'light' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches)
  if (dark) document.documentElement.classList.add('dark')
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
})()
