# Codex 额度监控

<img src="assets/icon-128.png" alt="Codex Quota Monitor" width="80" />

Windows 原生桌面与任务栏监控工具。查看当前 ChatGPT 套餐对应的 Codex 额度、Credits、重置到期、昨日及累计 token，也可把网速、CPU、内存、显卡、硬盘活动率放到任务栏。

**当前应用版本：1.7.2 · Windows x64 · 私有项目**

[下载安装包](https://github.com/wsk0303212/codex-quota-monitor/releases/latest) · [安装与卸载](docs/INSTALLATION.md) · [代码说明](docs/ARCHITECTURE.md) · [验证记录](docs/VALIDATION.md) · [更新记录](CHANGELOG.md)

## 能做什么

- 标题按登录账户显示套餐名称，支持详情小窗、系统托盘及任务栏模式。
- 展示额度剩余、下次恢复、Credits、可用重置次数和最近到期；到期与额度恢复分开说明。
- 昨日和历史累计 token 采用官方账户统计，按“万／亿／万亿”缩写，悬停显示完整整数。
- 六项系统指标独立每秒采样；账户刷新可选 1／5／15 分钟。
- 设置中的“任务栏内容”提供 11 个开关，保存后自动组合两行、调整宽度；全部取消时仍可从托盘打开。
- 字体按设备像素对齐，支持浅色／深色／跟随系统、50%～120% 窗口比例、右下角停靠和提醒。

任务栏默认顺序：

| 左 → 右 | 第 1 行 | 第 2 行 |
| --- | --- | --- |
| 1 | 上传速度 | 下载速度 |
| 2 | CPU | 内存 |
| 3 | 显卡 | 硬盘活动率 |
| 4 | 套餐名称 | 额度剩余 |
| 5 | Credits | 余额 |
| 6 | 重置次数 | 最近到期 |
| 7 | 昨日 token | 累计 token |

正常文字为绿色，低额度按阈值标黄；token 不因旧值变黄，失败用文字标记。全选为 598×40 逻辑像素，列间隔 4；任务栏没有足够空位时隐藏监控条，托盘入口保留。

## 快速开始

1. 准备 Windows 10／11 x64、.NET Framework 4.8+，单独安装 Codex CLI 并登录要查看的 ChatGPT 账户。
2. 从 [Release](https://github.com/wsk0303212/codex-quota-monitor/releases/latest) 下载 ZIP，核对同页 SHA-256 清单后完整解压。
3. 便携使用：双击 `CodexQuotaMonitor.exe`。一键安装：双击 `Install.cmd`，安装到当前用户目录，不需要管理员权限。
4. 找不到 CLI 时，在设置中选择真实 `codex.exe`。任务栏显示、靠左及各项内容都可在设置中选择。

鼠标停留约半秒显示详情；点击任务栏文字打开主面板，右键打开菜单。窗口关闭只收进托盘，从托盘右键“退出”结束程序。Ctrl＋滚轮或 Ctrl＋加／减可调整详情窗口比例。

安装包包含 exe、安装／卸载脚本、说明及文件校验和。它不是 MSI 或带向导的 Setup.exe；便携 exe 和 ZIP 暂未进行代码签名。安装不会自动登录、启用开机启动或启动应用。

## 数据与隐私

通过本机 `codex.exe app-server` 的账户接口读取数据，不发起模型推理、不购买 Credits、不消费重置券，也不扫描聊天日志计算 token。刷新本身不产生模型推理 token。

昨日按北京时间前一天匹配服务返回的日期，但官方每日桶的日界和延迟不由本程序保证。缺失值显示“暂未更新”或 `—`，不会伪造为 0。CLI 更新或账户接口不支持时，额度与 token 可以独立失败并保留旧值。

配置保存在 `%LOCALAPPDATA%\CodexQuotaMonitor\settings.json`。仓库和 Release 不包含本机配置、登录文件、真实账户截图或历史恢复包。详见 [隐私说明](PRIVACY.md)。

## 编译与打包

使用 Windows 自带的 .NET Framework C# 编译器，不需要 .NET SDK、Node 项目依赖或 NuGet 下载：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\CodexQuotaMonitor.exe --self-test
```

独立输出与发布 ZIP：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -OutputDirectory .\artifacts\build
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\package.ps1
```

发布结果在 `artifacts/releases`，已有同版本 ZIP 不会被静默覆盖。源码结构和 RPC 边界见 [代码说明](docs/ARCHITECTURE.md)，检查范围与已知限制见 [验证记录](docs/VALIDATION.md)。

2026-10-14 的应用界面验收为 45 项数据检查、57 项界面检查通过；2026-10-16 分发准备重新编译并验证离线数据及安装包，不把历史界面验收冒充本次全量复测。没有配置自动触发的云端 CI。

## 问题反馈与授权

在本私有仓库提交 Issue，附应用／Windows／CLI 版本及复现步骤；不要附 `auth.json`、配置、原始账户响应或未经检查的截图。

本项目不是 OpenAI 官方应用。仓库未授予开源许可，授权范围见 [项目声明](NOTICE.md)。