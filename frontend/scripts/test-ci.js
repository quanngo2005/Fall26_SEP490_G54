const { existsSync } = require('node:fs');
const { spawn, spawnSync } = require('node:child_process');

if (!process.env.CHROME_BIN && process.platform === 'win32') {
  const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  if (existsSync(edge)) {
    process.env.CHROME_BIN = edge;
  }
}

const cli = require.resolve('@angular/cli/bin/ng');
const child = spawn(
  process.execPath,
  // Extra arguments (e.g. --code-coverage from the review script) are passed through to ng test.
  [cli, 'test', '--watch=false', '--browsers=ChromeHeadless', ...process.argv.slice(2)],
  // detached on Unix makes ng a process-group leader, so the whole group can be killed below.
  { stdio: 'inherit', env: process.env, detached: process.platform !== 'win32' },
);

// A browser that exists but cannot launch would otherwise hang the run; a normal run takes about a minute.
// On timeout the whole tree (ng, Karma, the browser) is killed so nothing keeps port 9876 busy.
const timer = setTimeout(
  () => {
    console.error('ng test did not finish within 10 minutes; stopping it and its child processes.');
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
    } else {
      process.kill(-child.pid, 'SIGKILL');
    }
  },
  10 * 60 * 1000,
);

// The detached group no longer gets Ctrl+C from the terminal, so pass interrupts on to it.
if (process.platform !== 'win32') {
  for (const signal of ['SIGINT', 'SIGTERM']) {
    process.on(signal, () => process.kill(-child.pid, signal));
  }
}

child.on('exit', (code) => {
  clearTimeout(timer);
  process.exit(code ?? 1);
});
