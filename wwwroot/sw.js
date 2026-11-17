const CACHE_NAME = 'hali-yikama-shell-v1';
const APP_SHELL = [
    '/',
    '/index.html',
    '/driver.html',
    '/login.html',
    '/manifest.json',
    '/manifest-driver.json',
    '/icons/icon-office-192.png',
    '/icons/icon-office-512.png',
    '/icons/icon-driver-192.png',
    '/icons/icon-driver-512.png'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(APP_SHELL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(
                keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', (event) => {
    const req = event.request;
    if (req.method !== 'GET') return;

    const url = new URL(req.url);

    // API, health ve Swagger isteklerini asla önbelleğe alma
    if (url.origin !== self.location.origin ||
        url.pathname.startsWith('/api/') ||
        url.pathname.startsWith('/health') ||
        url.pathname.startsWith('/swagger')) {
        return;
    }

    // Network-first: çevrimiçiyken her zaman güncel dosya, çevrimdışıyken önbellekten kabuk
    event.respondWith(
        fetch(req)
            .then((response) => {
                if (response && response.ok && response.type === 'basic') {
                    const copy = response.clone();
                    caches.open(CACHE_NAME).then((cache) => cache.put(req, copy));
                }
                return response;
            })
            .catch(async () => {
                const cached = await caches.match(req);
                if (cached) return cached;

                if (req.mode === 'navigate') {
                    return new Response(
                        '<!DOCTYPE html><html lang="tr"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Çevrimdışı</title><style>body{font-family:system-ui,sans-serif;background:#0f172a;color:#f8fafc;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;padding:24px;text-align:center}div{max-width:360px}button{margin-top:16px;padding:12px 24px;border:none;border-radius:10px;background:#3b82f6;color:#fff;font-weight:700;font-size:15px;cursor:pointer}</style></head><body><div><h2>İnternet Bağlantısı Yok</h2><p>Lütfen bağlantınızı kontrol edip tekrar deneyin.</p><button onclick="location.reload()">Tekrar Dene</button></div></body></html>',
                        { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } }
                    );
                }

                return new Response('', { status: 503 });
            })
    );
});
