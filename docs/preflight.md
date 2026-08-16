# Manual Windows SSH preflight

This is a manual preflight. It catches common false positives before opening a remote project, but it cannot prove full Desktop/app-server compatibility. The repository's local [reference-bridge self-test](../tests/Test-ReferenceBridge.ps1) only verifies compilation and selected argument rules; it is not an SSH/Desktop integration test.

Use placeholders from the main guide: `codex-win` is a concrete local SSH alias and no command below contains live credentials.

## 1. Resolve the SSH alias

In local PowerShell:

```powershell
ssh -G codex-win
```

Confirm the resolved host, user, and identity file are the intended values. Verify the host-key fingerprint through an independent trusted channel on the first connection; never bypass this with `StrictHostKeyChecking=no`.

## 2. Check key authentication and remote Codex

```powershell
ssh -T -o BatchMode=yes codex-win "codex --version"
```

This must return a Codex version without a password prompt. It verifies the key, basic remote command execution, and that `codex` is available. It does **not** prove that the Windows login-shell bridge can carry Codex's multiline POSIX bootstrap.

## 3. Check POSIX shell execution

Create a harmless POSIX payload and send it over SSH standard input:

```powershell
$posixProbe = @'
set -eu
printf '%s\n' 'CODEX_SSH_POSIX_OK'
'@

$posixProbe | & ssh.exe -T -o BatchMode=yes codex-win "sh -s"
```

Expected output is exactly one line:

```text
CODEX_SSH_POSIX_OK
```

If this fails after public-key authentication succeeds, resolve the remote login-shell/command-interpreter problem before adding the host to Desktop.

## 4. Check a clean stdin/stdout path

```powershell
$stdoutPath = Join-Path $env:TEMP 'codex-ssh-probe.stdout.txt'
$stderrPath = Join-Path $env:TEMP 'codex-ssh-probe.stderr.txt'

'CODEX_SSH_STDIO_OK' |
  & ssh.exe -T -o BatchMode=yes codex-win "cat" 1> $stdoutPath 2> $stderrPath

Get-Content -LiteralPath $stdoutPath -Raw
Get-Content -LiteralPath $stderrPath -Raw
```

Expected stdout is `CODEX_SSH_STDIO_OK` plus a newline, and stderr is empty. Remove the two temporary probe files manually after inspecting them. Do not reuse this redirection pattern around `codex app-server proxy`: its stdio is a protocol channel and must not be logged or transformed.

## 5. Open the remote project

Only after the preceding checks pass:

1. In the ChatGPT desktop app, add/enable the SSH alias.
2. Select the intended remote project directory.
3. Start a small read-only task.

If Desktop fails despite all checks passing, capture only sanitized metadata: Desktop package version, Codex CLI version, OpenSSH banner, shell name/version, exit status, and whether the host slept or lost network. Do not post raw SSH verbose output, app-server protocol data, tokens, private keys, real paths, or host details.

## Interpretation

| Result | Next action |
| --- | --- |
| Step 2 fails | Fix public-key authentication, account selection, ACLs, or `codex` availability. |
| Step 2 passes but 3/4 fails | Fix the Windows login-shell/command-interpreter boundary. A stock `cmd.exe` path is not adequate for a POSIX bootstrap. |
| All steps pass but Desktop fails | Check sleep/network state, app and CLI version drift, and sanitized logs. Do not delete a live control socket. |

See the [main guide](../README.md) and [threat model](security-model.md).
