# Security policy

## Scope

This repository is documentation for SSH-based Codex remote projects on Windows. It is not a hosted service, a remote-desktop product, or an official OpenAI project.

## Reporting a concern

Do not open a public issue containing credentials, private keys, access tokens, real hostnames, IP addresses, SSH fingerprints, logs, screenshots, or command captures.

For a documentation security concern, contact the repository owner privately through GitHub. Include a minimal sanitized reproduction and state which published file is affected.

## Safe contribution rules

- Never commit passwords, private keys, tokens, key fingerprints, or live connection output.
- Use placeholders such as `<remote-user>` and `<host-name-or-private-address>` in examples.
- Do not add raw `ssh -vvv`, `ssh -G`, app-server proxy, or `SSH_AUTH_SOCK` output.
- Do not recommend exposing an app-server transport to a public network.
