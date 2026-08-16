# Windows SSH 手動 preflight

這是手動 preflight。它可在加入遠端專案前抓出常見假陽性，但不能保證 Desktop/app-server 完整相容。本 repo 的本機 [reference-bridge self-test](../tests/Test-ReferenceBridge.ps1) 只驗證編譯與部分參數規則；它不是 SSH/Desktop 整合測試。

以下範例沿用主文件的 `codex-win-target` 具體 SSH alias，所有命令都不含實際認證資料。測試新 profile 時，請保留原本已能使用的 alias／key 作為復原連線。

## 1. 證明登入的是預期 Windows profile

在目標遠端 Windows 的預期桌面帳號中，以該已登入使用者開啟 PowerShell，執行：

```powershell
whoami
```

結果請自行保留，不要發佈。它才是 SSH alias 必須選到的帳號；主機名稱、既有可用 alias 與目前可見的桌面，不能互相當作帳號識別。

在本機確認 alias 登入的正是同一帳號：

```powershell
$alias = 'codex-win-target'
ssh -T -o BatchMode=yes $alias "whoami"
```

兩邊結果必須一致。若不一致，請在此停止：為目標帳號建立新的具體 alias 與專用 key。不可複製另一個帳號的 Codex auth、私鑰或整個 profile。Windows OpenSSH 的 `DefaultShell` 是主機層級設定；它只能改 shell 選擇，不能選擇桌面帳號。

## 2. 解析 SSH alias

在本機 PowerShell 執行：

```powershell
$alias = 'codex-win-target'
ssh -G $alias
```

確認解析出的 host、user 與 identity file 都是預期值。第一次連線時，要透過獨立且可信任的管道核對 host-key fingerprint；不要用 `StrictHostKeyChecking=no` 跳過它。

## 3. 檢查金鑰驗證與遠端 Codex

```powershell
ssh -T -o BatchMode=yes $alias "codex --version"
```

它必須無密碼提示地回傳 Codex 版本。這會驗證金鑰、基本遠端命令與 `codex` 是否可用；但**不能**證明 Windows login-shell bridge 能承載 Codex 的多行 POSIX bootstrap。

## 4. 檢查 POSIX shell 執行

建立無害的 POSIX payload，再透過 SSH 標準輸入送出：

```powershell
$posixProbe = @'
set -eu
printf '%s\n' 'CODEX_SSH_POSIX_OK'
'@

$posixProbe | & ssh.exe -T -o BatchMode=yes $alias "sh -s"
```

預期輸出剛好是一行：

```text
CODEX_SSH_POSIX_OK
```

若公開金鑰已驗證成功但這一步失敗，請先處理遠端 login shell／命令直譯器問題，再把主機加入 Desktop。

## 5. 檢查乾淨的 stdin/stdout 路徑

```powershell
$stdoutPath = Join-Path $env:TEMP 'codex-ssh-probe.stdout.txt'
$stderrPath = Join-Path $env:TEMP 'codex-ssh-probe.stderr.txt'

'CODEX_SSH_STDIO_OK' |
  & ssh.exe -T -o BatchMode=yes $alias "cat" 1> $stdoutPath 2> $stderrPath

Get-Content -LiteralPath $stdoutPath -Raw
Get-Content -LiteralPath $stderrPath -Raw
```

預期 stdout 是 `CODEX_SSH_STDIO_OK` 加換行，stderr 必須是空的。檢查完成後，手動刪除這兩個暫存檔。不可把此重導向方式套用在 `codex app-server proxy`：它的 stdio 是 protocol channel，不能記錄或轉換。

## 6. 檢查內層 login shell（Windows 建議必做）

Desktop workflow 可能以 login／interactive flags 呼叫遠端 `$SHELL`。僅 `codex --version` 或 `sh -s` 成功，不能證明這個內層 shell 安靜且與 POSIX 相容。

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

預期 stdout 剛好是 `CODEX_SSH_LOGIN_SHELL_OK` 加換行，stderr 必須是空的。若失敗或輸出非 TTY job-control 類警告，不可過濾或重導向真正的 protocol stream。先修正 login-shell path，或使用已審核且只對該帳號設定的 bridge 路徑，再重新執行 preflight。

## 7. 開啟遠端專案

只有上述檢查皆成功後才進行：

1. 在 ChatGPT Desktop 加入／啟用 SSH alias。
2. 選擇預期的遠端專案目錄。
3. 先執行一個小型唯讀任務。

如果所有檢查都通過但 Desktop 仍失敗，只收集去識別化 metadata：Desktop package 版本、Codex CLI 版本、OpenSSH banner、shell 名稱／版本、結束碼，以及主機是否休眠或失去網路。不可張貼原始 SSH verbose output、app-server protocol、Token、私鑰、真實路徑或主機資料。

## 判讀

| 結果 | 下一步 |
| --- | --- |
| 第 1 步失敗 | 先修正 alias 的 `User`／key 選擇，再變更 shell 或安裝 Codex。 |
| 第 3 步失敗 | 修正公開金鑰驗證、帳號選擇、ACL 或 `codex` 是否可用。 |
| 第 3 步通過，但第 4/5/6 步失敗 | 修正 Windows login shell／命令直譯器邊界。標準 `cmd.exe` 路徑不適合承載 POSIX bootstrap；會輸出雜訊的 interactive shell 也不適合 protocol。 |
| 所有步驟通過但 Desktop 失敗 | 檢查休眠／網路狀態、App 和 CLI 版本差異與去識別化 log。不可刪除活程序使用的 control socket。 |

請搭配[主文件](../README.zh-TW.md)與 [威脅模型](security-model.zh-TW.md)使用。
