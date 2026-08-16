# Security policy

## Scope

This repository contains documentation and a source-only reference bridge for SSH-based Codex remote projects on Windows. It is not a hosted service, a remote-desktop product, or an official OpenAI project.

## Reporting a concern

Do not open a public issue containing credentials, private keys, access tokens, real hostnames, IP addresses, SSH fingerprints, logs, screenshots, or command captures.

Use GitHub private vulnerability reporting if it is enabled for this repository. If it is not available, open only a **redacted contact request**—do not include the technical details—so the maintainer can establish a private channel.

## Safe contribution rules

- Never commit passwords, private keys, tokens, key fingerprints, or live connection output.
- Use placeholders such as `<remote-user>` and `<host-name-or-private-address>` in examples.
- Do not add raw `ssh -vvv`, `ssh -G`, app-server proxy, or `SSH_AUTH_SOCK` output.
- Do not add prebuilt bridge binaries, Git Bash/Codex binaries, command payloads, or bridge runtime files.
- Do not recommend exposing an app-server transport to a public network.
- Do not recommend disabling Windows Firewall, using `StrictHostKeyChecking=no`, or copying Codex authentication files between hosts.
