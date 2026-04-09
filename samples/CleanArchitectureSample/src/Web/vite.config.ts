import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

// When running under Aspire, WithHttpsDeveloperCertificate() handles HTTPS automatically.
// Only generate certs manually for standalone `npm run dev`.
const isAspire = !!env.PORT;
let httpsConfig: { key: Buffer; cert: Buffer } | undefined;

if (!isAspire) {
    const baseFolder =
        env.APPDATA !== undefined && env.APPDATA !== ''
            ? `${env.APPDATA}/ASP.NET/https`
            : `${env.HOME}/.aspnet/https`;

    const certificateName = "web.frontend";
    const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
    const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

    if (!fs.existsSync(baseFolder)) {
        fs.mkdirSync(baseFolder, { recursive: true });
    }

    if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
        if (0 !== child_process.spawnSync('dotnet', [
            'dev-certs',
            'https',
            '--export-path',
            certFilePath,
            '--format',
            'Pem',
            '--no-password',
        ], { stdio: 'inherit', }).status) {
            throw new Error("Could not create certificate.");
        }
    }

    httpsConfig = {
        key: fs.readFileSync(keyFilePath),
        cert: fs.readFileSync(certFilePath),
    };
}

function firstDefined(values: Array<string | undefined>): string | undefined {
    return values.find(v => typeof v === 'string' && v.trim().length > 0);
}

function getAspireApiEndpoint(): string | undefined {
    const entries = Object.entries(env);

    // Aspire service discovery variables for referenced services, e.g. SERVICES__API__HTTPS__0
    const httpsEntry = entries.find(([key, value]) =>
        /^services__api__https__\d+$/i.test(key) && typeof value === 'string' && value.length > 0);
    if (httpsEntry?.[1]) {
        return httpsEntry[1];
    }

    const httpEntry = entries.find(([key, value]) =>
        /^services__api__http__\d+$/i.test(key) && typeof value === 'string' && value.length > 0);
    if (httpEntry?.[1]) {
        return httpEntry[1];
    }

    return undefined;
}

// Backend URL for the Vite dev-server proxy (server-side only, never bundled into client code).
const target = firstDefined([
    env.API_PROXY_TARGET,
    getAspireApiEndpoint(),
    env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` : undefined,
    env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : undefined,
    'https://localhost:5099'
]);

export default defineConfig({
    plugins: [tailwindcss(), sveltekit()],
    server: {
        port: env.PORT ? Number(env.PORT) : 5173,
        strictPort: false,
        https: httpsConfig,
        proxy: {
            // Proxy API requests to the backend
            '^/api': {
                target,
                secure: false
            },
            // Proxy OpenAPI endpoints
            '^/openapi': {
                target,
                secure: false
            },
            // Proxy Scalar API reference
            '^/scalar': {
                target,
                secure: false
            }
        }
    }
});
