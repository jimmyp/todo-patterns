// TodoList.Web/Client/wwwroot/service-worker.published.js
// Production service worker — cache-first for app shell, network-only for API
importScripts('./service-worker-assets.js');

const CACHE_NAME = `todolist-v${self.assetsManifest.version}`;
const APP_SHELL = self.assetsManifest.assets.map(a => a.url);

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(APP_SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(
                keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);

    // Network-only for API and auth — never cache these
    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/auth/')) {
        return; // falls through to network
    }

    // Cache-first for app shell assets
    event.respondWith(
        caches.match(event.request)
            .then(cached => cached ?? fetch(event.request))
    );
});
