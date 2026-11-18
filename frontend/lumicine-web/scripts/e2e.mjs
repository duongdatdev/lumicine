import { Client } from 'pg';
import { generateKeyPairSync } from 'node:crypto';
import { spawnSync } from 'node:child_process';

// This test account is only accepted on the dedicated local PostgreSQL listener.
const admin = { host: '127.0.0.1', port: 55439, database: 'postgres', user: 'lumicine_test' };
const database = `lumi_e2e_${process.pid}_${Date.now().toString(36)}`;
const e2ePassword = 'Local-E2E-Only-Password!9';
const adminClient = new Client(admin);
const testConnection = `Host=${admin.host};Port=${admin.port};Database=${database};Username=${admin.user};Pooling=false`;
const { privateKey } = generateKeyPairSync('rsa', {
  modulusLength: 2048,
  privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
  publicKeyEncoding: { type: 'spki', format: 'pem' },
});

function run(command, args, env) {
  const result = spawnSync(command, args, { cwd: process.cwd(), env, stdio: 'inherit', shell: process.platform === 'win32' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} exited with status ${result.status}`);
}

await adminClient.connect();
try {
  await adminClient.query(`CREATE DATABASE "${database}"`);
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    Logging__LogLevel__Default: 'Warning',
    Logging__LogLevel__Microsoft: 'Warning',
    LUMICINE_E2E_CONFIGURED: '1',
    ConnectionStrings__LumiCine: testConnection,
    Jwt__Issuer: 'https://lumicine-e2e.local',
    Jwt__Audience: 'lumicine-e2e',
    Jwt__PrivateKeyPem: privateKey,
    AuthRateLimit__PermitLimit: '1000',
    E2eUsers__Password: e2ePassword,
    E2E_USER_PASSWORD: e2ePassword,
    // Force all email delivery to a local sink with unusable credentials.
    Email__Host: '127.0.0.1',
    Email__Port: '2525',
    Email__Username: '',
    Email__Password: '',
    Email__FromAddress: '',
    Ai__Gemini__ApiKey: '',
    VnPay__TmnCode: '',
    VnPay__HashSecret: '',
  };
  run('dotnet', ['tool', 'restore'], env);
  run('dotnet', ['ef', 'database', 'update', '--configuration', 'Release', '--project', '../../backend/LumiCine.Infrastructure', '--startup-project', '../../backend/LumiCine.API'], env);
  run('dotnet', ['run', '--configuration', 'Release', '--no-launch-profile', '--project', '../../backend/LumiCine.API', '--', '--seed-demo'], env);
  run('dotnet', ['run', '--configuration', 'Release', '--no-launch-profile', '--project', '../../backend/LumiCine.API', '--', '--seed-acceptance'], env);
  run('npx', ['playwright', 'test', '--config', 'playwright.config.ts'], env);
} finally {
  await adminClient.query(`DROP DATABASE IF EXISTS "${database}" WITH (FORCE)`);
  await adminClient.end();
}
