import { readFile } from 'node:fs/promises';
import { extname, resolve } from 'node:path';
import { assetPath } from '../../build/verifyRelease.mjs';

const contentTypes = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.wasm': 'application/wasm',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
};

export async function installReleaseArtifact(context, directory, baseURL) {
  const root = resolve(directory);
  await readFile(assetPath(root, 'index.html'));
  const origin = new URL(baseURL).origin;
  await context.route(
    url => url.origin === origin,
    async route => {
      const request = route.request();
      const pathname = decodeURIComponent(new URL(request.url()).pathname);
      if (
        pathname === '/api' ||
        pathname.startsWith('/api/') ||
        !['GET', 'HEAD'].includes(request.method())
      ) {
        await route.continue();
        return;
      }
      const name = pathname.replace(/^\//, '') || 'index.html';
      try {
        let file = assetPath(root, name);
        let body;
        try {
          body = await readFile(file);
        } catch (error) {
          if (
            error.code !== 'ENOENT' ||
            !request.isNavigationRequest() ||
            extname(name)
          )
            throw error;
          file = assetPath(root, 'index.html');
          body = await readFile(file);
        }
        await route.fulfill({
          status: 200,
          body,
          contentType:
            contentTypes[extname(file)] ?? 'application/octet-stream',
          headers: { 'Cache-Control': 'no-store' },
        });
      } catch {
        await route.fulfill({
          status: 404,
          body: 'Staged release asset not found',
        });
      }
    },
  );
}
