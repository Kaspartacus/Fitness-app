import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const origin = process.env.FITNESS_BROWSER_ORIGIN;
const password = process.env.FITNESS_BROWSER_PASSWORD;
if (origin !== 'https://127.0.0.1:47192' || !/^Browser!1a-[0-9a-f]{48}$/.test(password ?? '')) {
  throw new Error('Start the isolated test host through Playwright.');
}

const workspace = await mkdtemp(join(tmpdir(), 'fitnessapp-browser-'));
const certificate = join(workspace, 'localhost.pem');
const certificateKey = join(workspace, 'localhost.key');
const project = fileURLToPath(new URL('../../src/FitnessApp.Server/FitnessApp.Server.csproj', import.meta.url));
const serverDirectory = dirname(project);
const server = join(serverDirectory, 'bin', 'Debug', 'net10.0', 'FitnessApp.Server.dll');
const root = fileURLToPath(new URL('../../', import.meta.url));

let child;
let stopping = false;
process.on('SIGTERM', () => {
  stopping = true;
  child?.kill();
});

function run(command, args, options = {}) {
  if (stopping) return Promise.reject(new Error('Test host stopped.'));
  return new Promise((resolve, reject) => {
    child = spawn(command, args, { stdio: ['ignore', 'inherit', 'inherit'], ...options });
    child.once('error', reject);
    child.once('close', (code) => {
      child = undefined;
      if (code === 0 || stopping) resolve();
      else reject(new Error(`${command} exited with status ${code}.`));
    });
  });
}

try {
  await run('dotnet', ['build', project, '--configuration', 'Debug',
    '--disable-build-servers', '-m:1', '-p:BuildInParallel=false'], { cwd: root });
  await run('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-noenc', '-days', '1',
    '-subj', '/CN=localhost', '-addext', 'subjectAltName=IP:127.0.0.1',
    '-out', certificate, '-keyout', certificateKey], { stdio: 'ignore' });

  // Allow only runtime settings explicitly created for this invocation.
  const runtime = {
    cwd: serverDirectory,
    env: {
      PATH: process.env.PATH,
      // Development loads the hosted client's local static assets. Use a fresh
      // home directory so it cannot load the developer's User Secrets.
      HOME: workspace,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: origin,
      Kestrel__Certificates__Default__Path: certificate,
      Kestrel__Certificates__Default__KeyPath: certificateKey,
      ConnectionStrings__DefaultConnection: `Data Source="${join(workspace, 'browser-tests.db').replaceAll('"', '""')}";Pooling=False`,
      DataProtection__KeyRingPath: join(workspace, 'key-ring'),
      Authentication__Jwt__SigningKey: randomBytes(48).toString('base64'),
      PublicApp__BaseUrl: origin,
      Email__Transport: 'Pickup',
      Email__PickupDirectory: join(workspace, 'email-pickup'),
      Logging__LogLevel__Default: 'Warning',
      BootstrapAdmin__Email: 'browser-admin@example.test',
      BootstrapAdmin__DisplayName: 'Testadministrator',
      BootstrapAdmin__Password: password,
    },
  };

  await run('dotnet', [server, 'bootstrap-admin'], runtime);
  delete runtime.env.BootstrapAdmin__Password;
  await run('dotnet', [server], runtime);
} catch (error) {
  if (!stopping) {
    console.error(error.message);
    process.exitCode = 1;
  }
} finally {
  await rm(workspace, { recursive: true, force: true });
}
