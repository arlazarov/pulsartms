import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function stampStyleVersion(index, css) {
  const version = createHash('sha256').update(css).digest('hex').slice(0, 16);
  const pattern = /href="css\/main\.css(?:\?v=[^"\s]*)?"/g;
  if ([...index.matchAll(pattern)].length !== 1)
    throw new Error('Expected one main stylesheet link.');
  return index.replace(pattern, `href="css/main.css?v=${version}"`);
}

if (
  process.argv[1] &&
  resolve(process.argv[1]) === fileURLToPath(import.meta.url)
) {
  const indexPath = new URL('../wwwroot/index.html', import.meta.url);
  const [index, css] = await Promise.all([
    readFile(indexPath, 'utf8'),
    readFile(new URL('../wwwroot/css/main.css', import.meta.url)),
  ]);
  const stamped = stampStyleVersion(index, css);
  if (stamped !== index) await writeFile(indexPath, stamped);
}
