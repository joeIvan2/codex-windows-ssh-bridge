# Experimental reference bridge

> **Status:** community reference source, not an OpenAI-supported component.
> It is intentionally not a prebuilt download or a password-free shortcut
> around Windows authorization. Test it in an isolated, authorized environment
> before using it with a real remote project.

[繁體中文](reference-bridge.zh-TW.md)

## Why this exists

Some Windows OpenSSH Server deployments start remote commands through
`cmd.exe`. A simple command such as `codex --version` may succeed, while a
multiline POSIX bootstrap issued by the ChatGPT desktop app is parsed or
truncated first. This reference is an advanced workaround for that narrow
command-interpreter boundary.

OpenAI documents generic SSH-host remote projects. It requires a concrete SSH
alias and a remote login shell where `codex` is on `PATH`, but it does not
prescribe Git Bash or this bridge. Prefer an administrator-reviewed
POSIX-compatible default login shell when that is practical.

## Security boundary

This bridge does **not** reduce the Windows account's permissions. A dedicated
key that reaches a forced command accepting `SSH_ORIGINAL_COMMAND` can execute
commands as the remote Windows user. Treat compromise of that key as compromise
of that account.

Before proceeding:

- Use a low-privilege remote account with restricted NTFS access.
- Create a separate bridge key and keep a tested, unforced recovery key.
- Verify the SSH host-key fingerprint independently; do not use
  `StrictHostKeyChecking=no`.
- Limit TCP/22 to intended LAN, VPN, or mesh peers with a narrow Windows
  Firewall rule. Do not disable the firewall broadly.
- Never copy Codex authentication data or private keys between hosts.

For the full threat model, read [security-model.md](security-model.md).

## What the source does

[`src/CodexSshBridge.cs`](../src/CodexSshBridge.cs) is compiled twice:

| Executable | Purpose |
| --- | --- |
| `codex-ssh-bridge.exe` | The `authorized_keys` forced command. It takes the unmodified `SSH_ORIGINAL_COMMAND`, writes one UTF-8-without-BOM temporary script in the remote user's local profile, and launches the real Bash executable directly. |
| `codex-ssh-login-shell.exe` | The `$SHELL` shim. It removes login/interactive flags before invoking Bash, preventing Git Bash's non-TTY interactive warnings from polluting the protocol stream. |

Both paths duplicate the existing SSH stdin/stdout/stderr handles and use
`STARTF_USESTDHANDLES`. They do not invoke `cmd.exe`, PowerShell, shell
profiles, `bash -s`, a console logger, or a protocol proxy. On success they emit
no extra bytes. The bridge clears shell startup variables such as `BASH_ENV` and
deletes the exact temporary file it created on exit.

The code intentionally has no command or protocol logging option. A crashed
process can leave a temporary script behind; its directory is inside the
remote user's profile, so protect that profile and clean only files after
confirming no live bridge session owns them.

## Build and local checks

Run these commands on the **remote Windows host** from a checkout of this
repository. The build script uses the Windows .NET Framework C# compiler; no
NuGet package, password, key, or host address is needed.

```powershell
.\tests\Test-ReferenceBridge.ps1
.\build\Build-ReferenceBridge.ps1 -OutputDirectory C:\Staging\CodexSshBridge
```

The test builds both executables and runs their limited self-tests. It does
not prove SSH transport, Git Bash behavior, or ChatGPT desktop compatibility.
Do not publish the generated `.exe` files.

## Manual deployment; no installer is included

This repository deliberately does **not** contain an installer. An elevated
administrator who understands the host's ACL policy must review and perform
the deployment manually:

1. Run the local checks and build the executables.
2. Create a **new**, administrator-controlled local directory, for example
   `C:\ProgramData\CodexSshBridge`. Do not reuse an existing directory or follow a
   junction/reparse point.
3. Give `SYSTEM` and Administrators write access. Give only the intended remote
   Windows user read/execute access. No other account may modify the directory.
4. Copy the two generated executables there. Copy
   `src\bridge.ini.example` beside them as `bridge.ini`, then edit only the
   local Bash path and the POSIX path to the sibling login-shell shim.
5. Confirm the configured `bash.exe` exists at a local, non-UNC path. Run each
   executable's `--self-test`, then run the [manual SSH preflight](preflight.md).

The self-test checks selected source behaviors; it does not load `bridge.ini` or
prove Git Bash, SSH transport, or ChatGPT desktop compatibility. Do not edit
`authorized_keys`, `sshd_config`, firewall rules, or password authentication
until the recovery path and full preflight have succeeded.

## Add only a dedicated bridge key

Keep the recovery key session open. In a separate administrative session, add
a **new** public-key line to the remote user's `authorized_keys`, prefixing only
that new line with:

```text
command="C:/ProgramData/CodexSshBridge/codex-ssh-bridge.exe"
```

The resulting line has this shape:

```text
command="C:/ProgramData/CodexSshBridge/codex-ssh-bridge.exe" ssh-ed25519 <public-key-data> codex-bridge
```

Use your newly generated bridge public key in place of the placeholder. Do not
publish a complete public-key line, and do not replace an existing recovery
key. `restrict`, `no-pty`, and `from=` are optional hardening controls
whose compatibility must be validated against the actual desktop workflow
before using them.

Run the [manual SSH preflight](preflight.md) in a second session, then add the
concrete alias to the ChatGPT desktop app. Disable password authentication only
after normal key access, recovery access, and a real remote-project task all
succeed.

## Roll back safely

1. Sign in with the unforced recovery key.
2. Remove only the exact forced bridge-key line from `authorized_keys`.
3. Verify that the bridge key is rejected and the recovery key still works.
4. Only then remove `C:\ProgramData\CodexSshBridge` and any confirmed stale
   temporary bridge files.

Never delete the bridge executable first: that leaves the forced key unusable
without restoring ordinary access.

## Validation still required

Before calling a deployment successful, verify all of the following on your
own host:

- Public-key login with no password prompt.
- A multiline POSIX probe and a clean stdin/stdout round trip.
- The ChatGPT desktop app can open the intended remote project and complete a
  small read-only task.
- Reconnection after sleep or a network interruption.
- The desktop app, Codex CLI, Windows OpenSSH, and shell versions are recorded.

If a connection fails, collect only sanitized metadata. Do not publish raw SSH
verbose output, command payloads, app-server protocol bytes, keys, tokens,
hostnames, IP addresses, or user-profile paths.
