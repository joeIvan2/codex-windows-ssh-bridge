# Windows SSH 遠端專案的威脅模型

本文件是說明，不是 sandbox 或權限控制產品。SSH 遠端專案會使用遠端 Windows 帳號所擁有的權限執行。

## 資產與信任邊界

- **本機私鑰：** 證明可存取遠端帳號；僅能保留在本機。
- **遠端 Windows 帳號：** 決定 Codex 可以讀取、修改與執行什麼；應使用低權限專用帳號。
- **遠端專案與相鄰資料：** 以 NTFS ACL 保護；不要讓該帳號廣泛存取個人設定檔、共用磁碟或管理員位置。
- **Codex 認證與 Token：** 留在預定主機／帳號；不可寫入本 repo，也不可在主機之間複製。

## 必要控制項

### SSH server 與網路

- 第一次連線時，以獨立管道核對 SSH host-key fingerprint。
- TCP/22 入站僅允許已知的私有 LAN、VPN 或 mesh peer；不可廣泛停用 Windows Firewall。
- 不可將 Codex app-server transport、WebSocket 或 control socket 公開到網際網路。
- 收緊任何驗證策略前，先保留並測試一般的復原 key／路徑。

### 金鑰生命週期

- 每把 key 都要有可識別的擁有者、預期裝置、建立日與檢視／到期日。
- 金鑰遺失或停用時，從 `authorized_keys` 移除它的完整對應行，並驗證它無法再通過驗證。
- 輪替時，先加入並測試新 key，再撤銷舊 key。

### Log 與支援

- log 可以保留時間、來源標籤與結束碼。
- 不可記錄私鑰、Token、完整 public-key 行、原始 command payload、app-server protocol bytes、`SSH_AUTH_SOCK` 目標或完整 `ssh -vvv` 輸出。
- 去識別化的支援回報應只包含版本與現象。

## 進階 bridge 的限制

獨立 key 不會自動限制為只能操作 Codex。特別是接受任意 `SSH_ORIGINAL_COMMAND` 的 forced-command bridge，仍可用遠端 Windows 使用者身分執行任意命令。它用於降低命令解析不相容問題，不是權限邊界。

如果受控環境核准使用進階 bridge：

- 必須原樣傳遞 SSH stdin/stdout/stderr。不可經過 PowerShell、`cmd.exe`、`tee`、console logger 或 shell profile。
- app-server 路徑不可使用 `bash -s`，因 stdin 需要保留給 protocol。
- 不可記錄原始 command 或 protocol 資料。
- `restrict`、`no-pty`、`from=` 等 key option 有相容性風險；必須以實際 Desktop workflow 整合測試後才可依賴。
- 保留未 forced 的復原 key 與 rollback 程序。

本 repo 包含僅供參考的實驗性 bridge 原始碼。它不是 sandbox、權限控制邊界，也不是 OpenAI 支援的元件。
