# 透過 SSH 在 Windows 使用 Codex 遠端專案

> 這是社群文件：讓 ChatGPT Desktop 透過 SSH 連到另一台 Windows 主機上的專案，且不把 Codex app-server 流量公開到網際網路。

[English](README.md)

> **範圍：** 這不是 OpenAI 官方專案。本文件說明 Windows SSH 遠端專案，不是遠端桌面控制工具，也不會繞過既有的 Windows 授權機制。

## 這是什麼／不是什麼

| 功能 | 本文件涵蓋？ | 說明 |
| --- | --- | --- |
| SSH 遠端專案 | 是 | Codex 對話會在遠端主機執行指令、讀取與修改檔案。 |
| ChatGPT Remote Control | 否 | 這是另一套已登入桌面主機的裝置配對功能。 |
| 公開 app-server endpoint | 否 | 不應將 app-server transport 暴露到公網或不受信任的共享網路。 |
| 設定內放密碼 | 否 | 使用 SSH 公開金鑰驗證；不要提交密碼、私鑰、Token 或真實主機資料。 |

## 運作架構

```mermaid
flowchart LR
  A[本機 ChatGPT Desktop] -->|SSH 私鑰驗證| B[遠端 Windows OpenSSH Server]
  B -->|遠端使用者登入 shell| C[遠端 Codex CLI]
  C -->|SSH 內的 app-server proxy| A
  D[遠端專案檔案與工具] --- C
```

Desktop 會從 `~/.ssh/config` 讀取具體 SSH alias、以 OpenSSH 解析，並透過遠端使用者的登入 shell 啟動 remote Codex app-server。因此 `codex` 必須能在該登入 shell 的 `PATH` 找到。

## 前置條件

### 本機電腦

- 有 Codex 存取權的 ChatGPT Desktop。
- OpenSSH client。
- 僅存在於本機使用者設定檔的專用 SSH 私鑰。

### 遠端 Windows 主機

- 正在執行 Windows OpenSSH Server，且只能由 LAN、VPN 或 mesh network 到達。
- 一個低權限的專用 Windows 帳號。
- 已在該遠端帳號下安裝並完成授權的 Codex CLI。
- 一個能成功執行 `codex --version` 的登入 shell。
- 遠端專案資料夾。

> Windows 注意事項：各台機器的 OpenSSH 設定不同。本文件以 Git Bash 這類 POSIX 相容 shell 為例，因為 remote bootstrap 是以 shell 為中心。加入 Codex 前，請先在自己的主機確認 shell 與 `PATH`。

## 快速設定

### 1. 在本機建立專用 SSH 金鑰

在本機 PowerShell 執行：

```powershell
ssh-keygen -t ed25519 `
  -f "$env:USERPROFILE\.ssh\id_ed25519_codex_win" `
  -C "codex-windows-remote"
```

`id_ed25519_codex_win` 是私鑰，僅留在本機。遠端只能安裝 `id_ed25519_codex_win.pub`。

### 2. 在遠端安裝公開金鑰

透過既有且安全的管理管道，將公開金鑰的完整單行內容加入：

```text
C:\Users\<remote-user>\.ssh\authorized_keys
```

`.ssh` 與 `authorized_keys` 要有嚴格的擁有者與 ACL。建議使用專用的一般帳號，不要使用管理員帳號。改動 server 驗證規則前，務必先以另一個工作階段驗證金鑰連線。

若你管理 SSH server 且要關閉密碼驗證，請只在金鑰與復原管道都驗證成功後設定：

```text
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
```

### 3. 在本機建立具體 SSH alias

建立或修改 `%USERPROFILE%\.ssh\config`：

```sshconfig
Host codex-win
    HostName <host-name-or-private-address>
    User <remote-user>
    IdentityFile ~/.ssh/id_ed25519_codex_win
    IdentitiesOnly yes
    PreferredAuthentications publickey
    PasswordAuthentication no
    KbdInteractiveAuthentication no
```

請使用像 `codex-win` 這樣具體的 alias；只有 pattern 的 `Host` 項目不會被 Codex 自動發現。

### 4. 在開啟 Desktop 前驗證

```powershell
ssh -o BatchMode=yes codex-win "codex --version"
ssh -G codex-win
```

登入遠端 shell 後，也應確認：

```bash
command -v codex
codex --version
printf 'SHELL=%s\n' "$SHELL"
```

### 5. 在 Desktop 加入遠端專案

1. 開啟 **Settings → Connections → SSH**。
2. 新增或啟用 `codex-win`。
3. 選擇遠端專案資料夾。
4. 在該遠端專案中開始對話。

指令、檔案、工具、認證與核准都屬於遠端主機及遠端帳號。不要手動公開 app-server transport，也不要建立公網 WebSocket endpoint。

## Windows 特有的排錯

| 現象 | 最可能意義 | 安全檢查方式 |
| --- | --- | --- |
| `Permission denied (publickey)` | 金鑰、帳號或 ACL 有問題 | `ssh -vvv codex-win` |
| Desktop 看不到主機 | alias 不具體，或設定無法解析 | `ssh -G codex-win` |
| 已驗證但出現 `codex: command not found` | 登入 shell 的 `PATH` 不完整 | 在遠端 shell 執行 `command -v codex` |
| 驗證後出現 `unexpected EOF` 或引號錯誤 | Windows SSH 可能將 POSIX bootstrap 交給不相容的命令直譯器 | 檢查登入 shell；bridge 只應作為進階且經檢視的 workaround |
| `socket hangup` | proxy 資料流受到雜訊污染，或殘留 app-server control process/socket | 保留僅含 metadata 的 log、只停止已驗證的殘留 Codex 程序，再重連 |

### 進階：Windows shell bridge

有些 Windows OpenSSH 安裝會預設使用 `cmd.exe`，它可能破壞多行 POSIX bootstrap 指令。首選方案是讓管理員設定一個受審核、POSIX 相容且 `PATH` 正確的預設登入 shell。

若無法這麼做，請使用**獨立的 Codex 專用 key**與經檢視的 native bridge。它必須原樣傳遞 SSH 的 stdin/stdout/stderr；保留未 forced 的復原 key；不可記錄原始 protocol stream。bridge 應被視為進階部署工件，不是通用的複製貼上解法。

官方 OpenAI 文件說明 SSH 遠端專案，但沒有指定 Git Bash 或特定 Windows forced-command bridge。

## 安全檢查清單

- [ ] 私鑰只保留在本機。
- [ ] Repo、SSH config 與 log 沒有密碼、私鑰、Token、真實主機名稱或 IP。
- [ ] 遠端帳號是低權限帳號，並與管理員帳號分開。
- [ ] SSH 僅限 LAN、VPN 或 mesh network。
- [ ] Codex app-server 未直接暴露在公網或共享網路。
- [ ] log 僅記錄時間、結束碼與來源標籤，不記錄原始 protocol 或 secrets。
- [ ] 啟用 forced-command bridge 前，已測試一般的復原 SSH key／路徑。

## 參考資料

- [OpenAI 文件：Remote connections](https://learn.chatgpt.com/docs/remote-connections)

## 授權條款

[MIT](LICENSE)
