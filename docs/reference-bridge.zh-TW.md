# 實驗性 reference bridge

> **狀態：** 社群參考原始碼，並非 OpenAI 支援元件。它不是預編譯下載檔，
> 也不是繞過 Windows 授權的「免密碼捷徑」。投入真實遠端專案前，請先在隔離且
> 已獲授權的環境測試。

[English](reference-bridge.md)

## 為何需要它

部分 Windows OpenSSH Server 會用 `cmd.exe` 啟動遠端命令。像
`codex --version` 這種簡單命令可能成功，但 ChatGPT Desktop 發出的多行
POSIX bootstrap 會先被錯誤解析或截斷。此 reference 只處理這個狹窄的
command-interpreter 邊界問題。

OpenAI 文件說明通用的 SSH 遠端專案：需要具體 SSH alias，以及能從 `PATH`
找到 `codex` 的遠端登入 shell；它沒有指定 Git Bash 或這個 bridge。若可行，
仍優先請管理員設定經審核的 POSIX 相容預設登入 shell。

## 部署前先選定 Windows 帳號

bridge 不會選擇、附著或控制可見的 Windows 桌面工作階段。它會以 SSH alias 的 `User` 與配對 public key 所選出的 Windows 帳號執行。該帳號各自擁有 profile、`%LOCALAPPDATA%`、Codex CLI／授權狀態、專案檔與 bridge 暫存 script 位置。

要為桌面 workflow 建置或部署 bridge 前：

1. 在預期 Windows 桌面上私下執行 `whoami`。
2. 為**完全相同的帳號**建立新的、具體的 alias 與專用 key；不可把原本可用於另一個 profile 的 alias 直接改指向它。
3. 驗證 `ssh -T -o BatchMode=yes <new-alias> "whoami"` 與桌面結果一致，再在同一帳號下安裝／授權 Codex，並完成[手動 preflight](preflight.zh-TW.md)。
4. 新 profile 完成實際的小型唯讀遠端專案任務前，保留舊 alias／key 作為復原連線。

`bridge.ini` 只選擇 shell executable，不能切換 Windows 帳號。Windows OpenSSH 的 `DefaultShell` 同樣是主機層級設定，不能選擇 user profile。若目標帳號不同於既有 bridge 帳號，應設定並驗證另一條 profile 專用連線，而不是複製 Codex 檔案或認證資料。

## 安全邊界

這個 bridge **不會**降低 Windows 帳號本身的權限。可抵達接受
`SSH_ORIGINAL_COMMAND` forced command 的獨立 key，仍能以遠端 Windows 使用者
身分執行命令。因此 key 外洩應視同該遠端帳號外洩。

開始前：

- 使用低權限遠端帳號，並以 NTFS 限制可存取範圍。
- 建立獨立的 bridge key，同時保留並測試一把未 forced 的 recovery key。
- 第一次連線要透過獨立管道驗證 SSH host-key fingerprint；不可使用
  `StrictHostKeyChecking=no`。
- 以精準 Windows Firewall 規則，將 TCP/22 限制在預期 LAN、VPN 或 mesh peer；
  不可廣泛關閉 firewall。
- 不可在主機間複製 Codex 認證資料或私鑰。

完整威脅模型請見 [security-model.zh-TW.md](security-model.zh-TW.md)。

## 原始碼做了什麼

[`src/CodexSshBridge.cs`](../src/CodexSshBridge.cs) 會編譯兩次：

| 執行檔 | 用途 |
| --- | --- |
| `codex-ssh-bridge.exe` | `authorized_keys` 的 forced command。它讀取未修改的 `SSH_ORIGINAL_COMMAND`，在遠端使用者 local profile 內寫入一份 UTF-8 無 BOM 的暫存 script，再直接啟動真正的 Bash。 |
| `codex-ssh-login-shell.exe` | `$SHELL` shim。它移除 login/interactive flags 後才啟動 Bash，以避免 Git Bash 的非 TTY interactive warning 污染 protocol stream。 |

兩條路徑都會 duplicate 原本 SSH 的 stdin/stdout/stderr handles，並使用
`STARTF_USESTDHANDLES`。它們不會呼叫 `cmd.exe`、PowerShell、shell profile、
`bash -s`、console logger 或 protocol proxy。成功時不輸出額外 bytes。bridge
會清除 `BASH_ENV` 等 shell startup 變數，並在結束時刪除自己建立的精確暫存檔。

程式刻意沒有 command 或 protocol logging 選項。程序異常結束時可能遺留暫存
script；該目錄位於遠端使用者 profile，必須保護此 profile，且只能在確認沒有
活 bridge session 擁有它後再清理。

## 編譯與本機檢查

以下命令要在**遠端 Windows 主機**、本 repo checkout 內執行。build script 使用
Windows 內附的 .NET Framework C# compiler；不需要 NuGet、密碼、金鑰或主機位址。

```powershell
.\tests\Test-ReferenceBridge.ps1
.\build\Build-ReferenceBridge.ps1 -OutputDirectory C:\Staging\CodexSshBridge
```

測試會編譯兩個執行檔並執行有限的 self-test；它無法證明 SSH transport、Git Bash
行為或 ChatGPT Desktop 相容性。不可發佈生成的 `.exe`。

## 手動部署；不提供 installer

本 repo 刻意**不提供 installer**。必須由了解該主機 ACL policy 的已提高權限管理員
人工審閱並完成部署：

1. 執行本機檢查並編譯執行檔。
2. 建立一個**新的**、由管理員控制的本機目錄，例如
   `C:\ProgramData\CodexSshBridge`。不可重用既有目錄，也不可跟隨
   junction/reparse point。
3. `SYSTEM` 與 Administrators 要有寫入權；只有預期的遠端 Windows 使用者應有
   讀取／執行權。其他帳號不可修改目錄。
4. 將兩個生成的執行檔複製到此目錄。把 `src\bridge.ini.example` 複製到同一處並
   改名為 `bridge.ini`，然後只修改本機 Bash path 與同目錄 login-shell shim 的
   POSIX path。
5. 確認設定的 `bash.exe` 是存在的本機非 UNC path。對兩個執行檔執行
   `--self-test`，再執行[手動 SSH preflight](preflight.zh-TW.md)。

編輯任何 key file 前，先由管理員在本機以 `sshd -T -C ...` 核對目標帳號實際套用的 `AuthorizedKeysFile`。Windows OpenSSH 可能因 administrator match rule 而使用不同路徑；不可假設每個帳號都使用 `%USERPROFILE%\\.ssh\\authorized_keys`，也不可公開產生的 path 或 output。

self-test 只檢查部分原始碼行為；它不會載入 `bridge.ini`，也不證明 Git Bash、
SSH transport 或 ChatGPT Desktop 相容。recovery path 與完整 preflight 成功前，
不可修改 `authorized_keys`、`sshd_config`、Firewall 規則或密碼驗證。

## 只加入專用的 bridge key

保持 recovery key 的連線工作階段開啟。在另一個已提高權限的工作階段，於選定目標帳號實際套用的
`authorized_keys` 新增**一行新的** public key，僅在這個新行前加上：

```text
command="C:/ProgramData/CodexSshBridge/codex-ssh-bridge.exe"
```

完成行的形狀是：

```text
command="C:/ProgramData/CodexSshBridge/codex-ssh-bridge.exe" ssh-ed25519 <public-key-data> codex-bridge
```

以新產生的 bridge public key 取代 placeholder。不可公開完整 public-key 行，也不可
取代既有 recovery key。`restrict`、`no-pty` 與 `from=` 都是選用的強化項目；
必須先確認實際 Desktop workflow 相容後才可使用。

在第二個工作階段執行[手動 SSH preflight](preflight.zh-TW.md)，再將具體 alias 加入
ChatGPT Desktop。只有一般金鑰、recovery key 及實際遠端專案工作都成功後，才可以
考慮關閉密碼驗證。

## 安全 rollback

1. 以未 forced 的 recovery key 登入。
2. 從 `authorized_keys` 移除**精確的** forced bridge-key 行。
3. 驗證 bridge key 被拒絕，同時 recovery key 仍可使用。
4. 最後才移除 `C:\ProgramData\CodexSshBridge` 與已確認無人使用的過期暫存 bridge 檔。

不可先刪 bridge executable；那會讓 forced key 失效，且尚未恢復一般存取。

## 仍需驗證

要宣布部署成功前，必須在自己的主機完成：

- 無密碼提示的 public-key 登入。
- 多行 POSIX probe 與乾淨 stdin/stdout round trip。
- ChatGPT Desktop 能開啟預期遠端專案並完成一個小型唯讀任務。
- 主機休眠或網路中斷後可以重新連線。
- 記錄 Desktop app、Codex CLI、Windows OpenSSH 與 shell 版本。

若連線失敗，只收集去識別化 metadata。不可公開原始 SSH verbose output、command
payload、app-server protocol bytes、key、Token、hostname、IP 或 user-profile path。
