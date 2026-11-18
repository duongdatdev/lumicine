import { spawn } from 'node:child_process';

const api = spawn('dotnet', [
  'run', '--configuration', 'Release', '--no-launch-profile',
  '--project', '../../backend/LumiCine.API', '--urls', 'https://localhost:7005',
], {
  cwd: process.cwd(),
  env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Testing' },
  stdio: 'inherit',
});

api.on('exit', (code) => process.exit(code ?? 1));
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => api.kill(signal));
}
