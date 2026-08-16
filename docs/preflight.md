# Manual Windows SSH preflight

This is a manual preflight. It catches common false positives before opening a remote project, but it cannot prove full Desktop/app-server compatibility. The repository's local [reference-bridge self-test](../tests/Test-ReferenceBridge.ps1) only verifies compilation and selected argument rules; it is not an SSH/Desktop integration test.

Use placeholders from the main guide: `codex-win-target` is a concrete local SSH alias and no command below contains live credentials. Keep the old working alias/key intact as recovery access while testing a new profile.

## 1. Prove the intended Windows profile

On the intended remote Windows desktop, open PowerShell as that signed-in user and run:

```powershell
whoami
```

Keep that result private. It is the account the SSH alias must select; the host name, a working old alias, and the visible desktop are not interchangeable account identifiers.

From the local computer, verify the alias reaches that same account:

```powershell
$alias = 'codex-win-target'
ssh -T -o BatchMode=yes $alias "whoami"
```

The two results must match. If they do not, stop here: create a new concrete alias and dedicated key for the intended account. Do not copy the other account's Codex auth files, private keys, or profile directory. A Windows OpenSSH `DefaultShell` setting is host-wide; it changes a shell choice, not the selected desktop account.

## 2. Resolve the SSH alias

In local PowerShell:

```powershell
$alias = 'codex-win-target'
ssh -G $alias
```

Confirm the resolved host, user, and identity file are the intended values. Verify the host-key fingerprint through an independent trusted channel on the first connection; never bypass this with `StrictHostKeyChecking=no`.

## 3. Check key authentication and remote Codex

```powershell
ssh -T -o BatchMode=yes $alias "codex --version"
```

This must return a Codex version without a password prompt. It verifies the key, basic remote command execution, and that `codex` is available. It does **not** prove that the Windows login-shell bridge can carry Codex's multiline POSIX bootstrap.

## 4. Check POSIX shell execution

Create a harmless POSIX payload and send it over SSH standard input:

```powershell
$posixProbe = @'
set -eu
printf '%s\n' 'CODEX_SSH_POSIX_OK'
'@

$posixProbe | & ssh.exe -T -o BatchMode=yes $alias "sh -s"
```

Expected output is exactly one line:

```text
CODEX_SSH_POSIX_OK
```

If this fails after public-key authentication succeeds, resolve the remote login-shell/command-interpreter problem before adding the host to Desktop.

## 5. Check a clean stdin/stdout path

```powershell
$stdoutPath = Join-Path $env:TEMP 'codex-ssh-probe.stdout.txt'
$stderrPath = Join-Path $env:TEMP 'codex-ssh-probe.stderr.txt'

'CODEX_SSH_STDIO_OK' |
  & ssh.exe -T -o BatchMode=yes $alias "cat" 1> $stdoutPath 2> $stderrPath

Get-Content -LiteralPath $stdoutPath -Raw
Get-Content -LiteralPath $stderrPath -Raw
```

Expected stdout is `CODEX_SSH_STDIO_OK` plus a newline, and stderr is empty. Remove the two temporary probe files manually after inspecting them. Do not reuse this redirection pattern around `codex app-server proxy`: its stdio is a protocol channel and must not be logged or transformed.

## 6. Check the inner login shell (recommended on Windows)

The desktop workflow can invoke the remote `$SHELL` with login/interactive flags. A basic `codex --version` or `sh -s` success does not prove that this nested shell is quiet and POSIX-compatible.

```powershell
$loginShellProbe = @'
set -eu
"$SHELL" -lic 'printf "%s\n" "CODEX_SSH_LOGIN_SHELL_OK"'
'@

$loginShellProbe |
  & ssh.exe -T -o BatchMode=yes $alias "sh -s" 1> $stdoutPath 2> $stderrPath

Get-Content -LiteralPath $stdoutPath -Raw
Get-Content -LiteralPath $stderrPath -Raw
```

Expected stdout is exactly `CODEX_SSH_LOGIN_SHELL_OK` plus a newline, and stderr is empty. If this fails or emits warnings such as non-TTY job-control messages, do not filter or redirect the real protocol stream. Fix the login-shell path or use the reviewed account-specific bridge path, then repeat the preflight.

## 7. Open the remote project

Only after the preceding checks pass:

1. In the ChatGPT desktop app, add/enable the SSH alias.
2. Select the intended remote project directory.
3. Start a small read-only task.

If Desktop fails despite all checks passing, capture only sanitized metadata: Desktop package version, Codex CLI version, OpenSSH banner, shell name/version, exit status, and whether the host slept or lost network. Do not post raw SSH verbose output, app-server protocol data, tokens, private keys, real paths, or host details.

## Interpretation

| Result | Next action |
| --- | --- |
| Step 1 fails | Fix the alias `User`/key selection before changing a shell or installing Codex. |
| Step 3 fails | Fix public-key authentication, account selection, ACLs, or `codex` availability. |
| Step 3 passes but 4/5/6 fails | Fix the Windows login-shell/command-interpreter boundary. A stock `cmd.exe` path is not adequate for a POSIX bootstrap, and a noisy interactive shell is not protocol-safe. |
| All steps pass but Desktop fails | Check sleep/network state, app and CLI version drift, and sanitized logs. Do not delete a live control socket. |

See the [main guide](../README.md) and [threat model](security-model.md).
