# Security Policy

## Supported versions

Security fixes are provided for the latest public Spode release. Older builds
may not receive fixes.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Use GitHub's private vulnerability reporting:

1. Open the repository on GitHub.
2. Go to **Security → Advisories**.
3. Click **Report a vulnerability**.

Include:

- Spode version and build;
- Windows version;
- steps to reproduce;
- expected and actual behaviour;
- logs or screenshots if safe to share.

Do not include passwords, certificates, tokens, or personal data.

## Scope

In scope:

- Spode desktop application;
- mod installation and profile handling;
- ModAPI download/cache/injection flow;
- update and release artifacts;
- bundled `ModAPI.DLLInjector.dll` handling.

Out of scope:

- vulnerabilities in Spore itself;
- third-party mods and their content;
- the official Spore ModAPI Launcher Kit;
- issues caused by modified builds or patched binaries.

## Disclosure

Please allow reasonable time to investigate and publish a fix before public
disclosure. Credit will be given unless you prefer to remain anonymous.
