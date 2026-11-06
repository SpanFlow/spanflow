/*
 * Studio's service worker — here so browsers that still ask for one offer "Install app".
 *
 * It caches nothing on purpose: Studio is an admin console over live data, and a stale shell after a
 * redeploy would run old code against a new API. Every request goes to the network exactly as it
 * would without a worker; the fetch handler exists only because some installers look for one.
 */
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))
self.addEventListener('fetch', () => {
  // No respondWith: the browser handles the request normally.
})
