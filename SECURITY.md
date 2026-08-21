# Security Policy

## Supported versions

Only the latest source revision and the latest published prerelease are supported. This project is still a demo; no security SLA is offered.

## Reporting a vulnerability

Do not open a public Issue for leaked credentials, arbitrary code execution, authentication bypass, or a report that contains private audio/transcripts.

Until a dedicated security email is configured, use GitHub's private vulnerability reporting feature on the repository. Before the first public release, the owner must enable **Settings → Security → Private vulnerability reporting**.

Include:

- affected version/commit;
- operating system;
- minimal reproduction steps;
- impact and attack prerequisites;
- sanitized logs without API keys, transcripts, or personal information.

## Secret-handling rules

- Never commit Tencent AppID/SecretID/SecretKey, DeepSeek API keys, `settings.dat`, databases, logs, recordings, or crash dumps.
- Use a least-privilege Tencent sub-account for local development.
- Revoke and rotate any key accidentally pasted into an Issue, commit, screenshot, or build artifact. Deleting the text is not sufficient.
- Public binaries must not contain maintainer-owned credentials.

## Known security limitations

- DPAPI/Android Keystore protect data at rest but not a compromised logged-in device.
- Windows text injection and clipboard fallback interact with other local processes.
- The Windows binary is not yet code-signed or automatically updated.
- Audio and transcripts are processed by configured third-party cloud providers.
- Generated meeting reports are untrusted AI output and must not be treated as authoritative instructions.
