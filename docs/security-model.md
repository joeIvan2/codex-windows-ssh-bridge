# Security model for Windows SSH remote projects

This guide is documentation, not a sandbox or access-control product. SSH remote projects run with the rights of the remote Windows account.

## Assets and trust boundary

- **Local private key:** proves access to the remote account. Keep it only on the local machine.
- **Remote Windows account:** determines what Codex can read, change, and execute. Make it a dedicated least-privilege account.
- **Remote project and adjacent data:** protect with NTFS ACLs; avoid giving the account broad access to personal profiles, shared drives, or administrator locations.
- **Codex authentication and tokens:** remain on their intended host/account. Do not copy them into this repository or between hosts.

## Required controls

### SSH server and network

- Verify the SSH host-key fingerprint out of band on first connection.
- Allow inbound TCP/22 only from known private LAN, VPN, or mesh peers. Do not broadly disable Windows Firewall.
- Do not expose Codex app-server transports, WebSockets, or control sockets to a public network.
- Use a normal recovery key/path and test it before tightening any authentication policy.

### Key lifecycle

- Give each key a human owner, intended device, creation date, and review/expiry date.
- Revoke a lost or retired key by removing its exact line from `authorized_keys`, then verify it can no longer authenticate.
- Rotate a key by adding and testing the replacement before revoking the old one.

### Logging and support

- Logs may contain timestamps, source labels, and exit codes.
- Never log raw private keys, tokens, full public-key lines, raw command payloads, app-server protocol bytes, `SSH_AUTH_SOCK` targets, or full `ssh -vvv` output.
- Sanitized support reports should contain versions and symptoms only.

## Advanced bridge limitation

A dedicated key is not automatically restricted to Codex. In particular, a forced-command bridge that accepts arbitrary `SSH_ORIGINAL_COMMAND` can execute arbitrary commands as the remote Windows user. It reduces command-parser incompatibility; it is not a privilege boundary.

If an advanced bridge is approved for a controlled environment:

- Preserve SSH stdin/stdout/stderr exactly. Do not proxy it through PowerShell, `cmd.exe`, `tee`, a console logger, or a shell profile.
- Do not use `bash -s` for the app-server path because stdin is needed for the protocol.
- Do not log raw command or protocol data.
- Treat key options such as `restrict`, `no-pty`, and `from=` as compatibility-sensitive. Test them against the actual Desktop workflow before relying on them.
- Keep an unforced recovery key and a rollback procedure.

The repository includes a source-only experimental bridge reference. It is not a sandbox, an access-control boundary, or an OpenAI-supported component.
