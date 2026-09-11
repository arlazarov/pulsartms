import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';

export function secretFindings(file, content) {
  const findings = [];
  if (/(?:^|\/)(?:\.env(?:\..+)?|secrets\.json|gmail-credentials\.json)(?:$|\/)/i.test(file)
      && !file.endsWith('.example')) findings.push('local credential file');
  if (/(?:^|\/)(?:gmail-token|artifacts|node_modules|bin|obj)\//i.test(file)) findings.push('generated or private directory');
  const signatures = [
    /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/,
    /\bAKIA[0-9A-Z]{16}\b/, /\bAIza[0-9A-Za-z_-]{35}\b/,
    /\bgh[pousr]_[A-Za-z0-9]{30,}\b/, /\b(?:samsara_api_|sk-proj-)[A-Za-z0-9_-]{15,}/,
    /postgres(?:ql)?:\/\/[^\s:@]+:[^\s@]+@/,
  ];
  if (signatures.some(pattern => pattern.test(content))) findings.push('credential signature');
  if (/appsettings.*\.json$/i.test(file)) {
    try {
      const inspect = value => {
        for (const [key, item] of Object.entries(value)) {
          if (item && typeof item === 'object') inspect(item);
          else if (/(?:apikey|secret|password|token|connectionstring|defaultconnection)$/i.test(key)
              && typeof item === 'string' && item.trim()) findings.push('populated secret configuration');
        }
      };
      inspect(JSON.parse(content));
    } catch { findings.push('unreadable configuration'); }
  }
  return [...new Set(findings)];
}

export function checkStaged() {
  const git = args => execFileSync('git', args, {maxBuffer: 20 * 1024 * 1024});
  const files = git(['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z']).toString().split('\0').filter(Boolean);
  const failures = [];
  for (const file of files) {
    const bytes = git(['show', ':' + file]);
    const reasons = secretFindings(file, bytes.toString());
    if (bytes.length > 1024 * 1024) reasons.push('file exceeds one MiB; review before tracking');
    if (reasons.length) failures.push(`${file}: ${reasons.join(', ')}`);
  }
  if (failures.length) throw new Error('Commit blocked:\n' + failures.join('\n'));
  console.log(`Staged safety check passed (${files.length} files). Pattern checks are not a security guarantee.`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { checkStaged(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
