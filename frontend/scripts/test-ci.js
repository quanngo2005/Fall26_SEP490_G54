const { existsSync } = require('node:fs');
const { spawnSync } = require('node:child_process');

if (!process.env.CHROME_BIN && process.platform === 'win32') {
  const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  if (existsSync(edge)) {
    process.env.CHROME_BIN = edge;
  }
}

const cli = require.resolve('@angular/cli/bin/ng');
const result = spawnSync(
  process.execPath,
  // Extra arguments (e.g. --code-coverage from the review script) are passed through to ng test.
  [cli, 'test', '--watch=false', '--browsers=ChromeHeadless', ...process.argv.slice(2)],
  {
    stdio: 'inherit',
    env: process.env,
  },
);

process.exit(result.status ?? 1);
