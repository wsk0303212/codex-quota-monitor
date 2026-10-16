# 验证与限制

## 本次 GitHub 分发准备

2026-10-16：从仓库源码使用现有 .NET Framework 编译器构建 1.7.3 发布文件，45 项离线检查通过，包含 MIT 文本的安装包通过 21 项隔离检查，并核对包内清单和 SHA-256。安装／卸载脚本不覆盖开发机正在使用的程序、快捷方式或卸载登记。实际结果见 Release 附件 `validation.json`。

## 已有界面验收

2026-10-14 的 1.7.2 已在本机执行 45 项数据检查、57 项界面检查，覆盖真实读取、缩放、任务栏文字可见、控件不交叠、指针悬停不抢焦点、移开隐藏、设置保存／取消、显示开关和宽度收缩。账户截图、当前数值、机器路径和运行日志不上传仓库。

这批是同一应用版本的分发整理；上面的界面结果为历史验收，不冒充 10 月 16 日重新执行全部桌面交互。

## 可复现检查

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\CodexQuotaMonitor.exe --self-test
```

`--self-test` 离线运行，不读取账户，结果位于输出目录中的 `test-results.json`。

```powershell
.\dist\CodexQuotaMonitor.exe --probe
.\dist\CodexQuotaMonitor.exe --system-probe
.\dist\CodexQuotaMonitor.exe --smoke
```

后面三项用于开发机验证：`--probe` 读取本机账户状态，`--system-probe` 采样本机系统指标，`--smoke` 会打开测试窗口、读取账户并短暂移动指针完成悬停检查。请在可用的已解锁桌面上运行 `--smoke`，先退出正常监控实例，避免空位竞争；不要把这些个人输出上传仓库。自定义输出目录运行界面检查时需先创建其上一级的 `evidence` 目录。

## 发布检查

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\test-package.ps1
```

打包脚本编译、执行离线测试、按白名单组装安装 ZIP，并输出包与 exe 的 hash；相同版本的既有输出不会静默覆盖。安装检查脚本在隔离目录覆盖安装、升级、卸载、保留无关文件、拒绝无所有权目录与篡改载荷，共 21 项检查；不操作真实快捷方式和卸载登记。可用 `-ReleaseDirectory` 指定另一输出目录。没有自动配置 GitHub Actions，避免上传或文档变化自动消耗云端 CI 配额。

## README 图片

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\tools\render-previews.ps1 -Executable .\dist\CodexQuotaMonitor.exe
```

预览脚本使用 exe 中的 WPF 模板及原生任务栏绘制代码，填充固定合成数据。面板动态区为预览脚本构造，图片只说明外观，不能代替运行验收。脚本不启动程序主入口或 CLI，不读取配置、不采样硬件、不截取桌面，不连接真实账户。

## 当前边界

- 本机验证不能代替其他 Windows 版本、物理多屏 DPI 切换和不同 GPU／网卡驱动验收。
- 全选布局需要足够任务栏空位；空间不足时会隐藏监控条。
- 系统指标来源和任务管理器采样时刻不同，瞬时百分比可能有差异。
- Codex CLI 的账户接口变化可能影响兼容性，未知字段保持未知。
- 昨日统计受官方每日桶和更新延迟影响，不能当作北京时间实时计费读数。
- 未进行代码签名；发布 ZIP 不包含 CLI、.NET 安装器或用户配置。
