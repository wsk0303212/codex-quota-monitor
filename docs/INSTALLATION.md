# 安装、升级与卸载

## 运行条件

- Windows 10／11 x64；已具备 .NET Framework 4.8 或更高版本的桌面环境。
- 单独安装 Codex CLI，并登录要查看的 ChatGPT 账户。本工具不包含 CLI、账户或 API Key。
- 自动找不到 CLI 时，在设置中选择真正的 `codex.exe`，而非 `.cmd` 包装脚本。

已验收的是特定 Windows、CLI 和账户组合，并不保证所有未来版本的账户接口均可用。CLI 使用 API Key 登录时无法据此提供 ChatGPT 订阅额度。

## 下载与校验

从仓库 [Releases](https://github.com/wsk0303212/codex-quota-monitor/releases) 下载 `CodexQuotaMonitor-1.7.3-win-x64.zip` 和旁边的 `SHA256SUMS.txt`。仓库现已公开，可直接下载。

```powershell
Get-FileHash .\CodexQuotaMonitor-1.7.3-win-x64.zip -Algorithm SHA256
```

将结果与 Release 中的校验值对比。ZIP 和 exe 暂未进行代码签名；校验和用于核对下载内容，不等同发行者签名。

## 便携运行

完整解压 ZIP，双击 `CodexQuotaMonitor.exe`。不要在压缩包预览窗口内直接运行。关闭窗口只收进托盘；从托盘右键“退出”才会结束程序。

## 一键安装

完整解压后双击 `Install.cmd`。它调用包内 PowerShell 脚本，默认安装到：

```text
%LOCALAPPDATA%\Programs\CodexQuotaMonitor
```

安装只针对当前用户，不需要管理员权限。创建英文名 `Codex Quota Monitor` 的桌面和开始菜单快捷方式，并登记当前用户卸载项。已有同名、指向其他程序的快捷方式会被保留。安装不会自动启动程序、登录 Codex 或启用开机启动。

第一次启动后可在设置中选择任务栏模式、靠左、主题、提醒、刷新间隔，以及 11 项显示开关。需要开机启动时自行勾选保存。

## 升级

1. 从旧程序托盘右键退出。
2. 解压新版 ZIP，在新解压目录运行 `Install.cmd`。
3. 启动快捷方式。旧 exe 另存为安装目录内的 `CodexQuotaMonitor.exe.previous`；已有上一版备份会被更新。

配置位于 `%LOCALAPPDATA%\CodexQuotaMonitor\settings.json`，升级保留它。安装脚本遇到没有本程序所有权标记的既有目录会停止，避免覆盖其他文件。

## 卸载

先从托盘退出，再通过 Windows“已安装的应用”中的卸载入口，或运行安装目录里的 `Uninstall.cmd`。只删除本安装器登记的已知文件、匹配的快捷方式与卸载项；匹配当前安装路径的开机启动项也会移除。目录中的其他文件保留。

卸载默认保留 `%LOCALAPPDATA%\CodexQuotaMonitor` 的用户配置，也不改动 Codex 登录。便携版没有卸载登记，退出后删除解压目录即可；若开启过开机启动，先在设置中关闭。

## 常见问题

| 现象 | 处理 |
| --- | --- |
| 找不到 CLI | 在设置选择 `codex.exe`；核对 CLI 已单独安装 |
| 无额度或 token | 核对 CLI 登录方式、账户权限及 CLI 接口支持；没有数据时不会显示假 0 |
| 昨日暂未更新 | 等待官方统计返回相应日期；不是本地实时日志计数 |
| 任务栏不显示 | 开启任务栏模式，减少勾选项目以留出空位，或从托盘切回桌面 |
| 窗口关闭后仍在运行 | 属于收进托盘；从托盘菜单退出 |
| 安装提示目标目录已存在 | 保留该目录，选择空目录，或核对是否为旧手工复制版本 |
| PowerShell 被组织策略阻止 | 使用便携 exe，或联系设备管理员；不要修改全局安全策略 |
