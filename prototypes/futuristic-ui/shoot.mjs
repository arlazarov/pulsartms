// Saves Fleet and Dispatch screenshots, light and dark, desktop and phone,
// with the labelled demo fixtures.
//   node scripts/artifacts.mjs run scratch -- \
//     node prototypes/futuristic-ui/shoot.mjs '{artifacts}'
import { writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { launch, sleep } from './cdp.mjs';

const out = process.argv[2];
const base = process.env.CONCEPT_URL ?? 'http://localhost:5179';
const truck = '00000000-0000-4000-8000-000000001000';
const sizes = { desktop: [1600, 940, false], mobile: [390, 844, true] };
const profile = join(out, 'profile');
const b = await launch(profile);
try {
  for (const theme of ['light', 'dark'])
    for (const [size, [w, h, mobile]] of Object.entries(sizes))
      for (const route of ['fleet', 'dispatch']) {
        await b.viewport(w, h, mobile);
        await b.send('Page.navigate', {
          url: `${base}/?fixtures=1&theme=${theme}&r=${Date.now()}#/${route}?truck=${truck}`,
        });
        await sleep(3500);
        const shot = await b.send('Page.captureScreenshot', { format: 'png' });
        const file = join(out, `${route}-${size}-${theme}.png`);
        writeFileSync(file, Buffer.from(shot.data, 'base64'));
        console.log(file);
      }
} finally {
  b.close();
  await sleep(300);
  rmSync(profile, { recursive: true, force: true });
}
