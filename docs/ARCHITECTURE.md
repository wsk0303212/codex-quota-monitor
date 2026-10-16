# 代码说明

这是 Windows x64 的原生 C# 桌面程序。详情和设置采用 WPF，任务栏文字与托盘采用 WinForms；XAML 和图标嵌入单个 exe。不依赖浏览器运行时或第三方 NuGet 包。

## 模块

| 文件 | 职责 |
| --- | --- |
| `src/Program.cs` | 程序入口、单实例、窗口和托盘事件、刷新调度、设置保存、界面检查 |
| `src/CodexClient.cs` | 本机 CLI 子进程、stdio JSON-RPC、方法白名单、请求超时与通知 |
| `src/Quota.cs` | 额度、Credits、重置到期解析与提醒去重 |
| `src/TokenUsage.cs` | 官方累计和每日 token 数据、昨日日期选择、中文单位 |
| `src/TaskbarContent.cs` | 两行额度文案、旧值标记、完整悬停内容 |
| `src/TaskbarColumns.cs` | 按开关组合列、统一列间隔和总宽度 |
| `src/TaskbarHost.cs` | 自有任务栏子窗口、空位选择、绘制、悬停、后台采样调度 |
| `src/SystemUsage.cs` | CPU、物理内存、网速、GPU 与磁盘活动率采样 |
| `src/PixelCoordinates.cs` | 原生窗口 DPI 上下文和物理像素坐标 |
| `src/DisplayLayout.cs` | WPF 字体与尺寸按设备像素对齐 |
| `src/Settings.cs` | 用户配置、原子保存、开机启动设置 |
| `src/MainWindow.xaml`、`src/SettingsWindow.xaml` | 主窗口与设置界面 |
| `src/Tests.cs` | 离线数据、日期、格式、布局和配置检查 |
| `build.ps1` | 调用系统 .NET Framework C# 编译器 |
| `tools/package.ps1`、`packaging/` | 发布 ZIP、一键安装与卸载脚本 |

## 数据流与边界

1. 程序寻找 `codex.exe`，也允许用户在设置中指定。
2. 创建自有 `codex.exe app-server` 子进程，通过标准输入输出通信。
3. `account/read`、`account/rateLimits/read`、`account/usage/read` 分别读取账户、额度和 token；收到更新通知后排队刷新。
4. 账户额度按 1／5／15 分钟更新。系统指标使用独立的每秒后台采样。
5. 解析结果进入详情窗口、任务栏和悬停；失败保留最近有效快照，并标记旧值。切换账户清空旧数据。

RPC 白名单还包含协议初始化及用户发起登录所需的 `account/login/start`、`account/login/cancel`。程序不发起模型推理、不购买 Credits、不消费重置券，也不调用登出接口。它会使用 CLI 的既有登录上下文，首次登录仍需要用户完成官方登录流程。

累计 token 采用服务返回的累计字段，不累加本地日志或每日桶。昨日按北京时间的前一天匹配服务日期；官方统计可能延迟，也未在本程序中承诺其日界为北京时间。缺失值显示未知，不替换成 0。

## 系统指标

- CPU：`GetSystemTimes` 的时间增量，内核时间包含空闲时间。
- 内存：`GlobalMemoryStatusEx` 的物理总量与可用量。
- 网速：活动以太网／Wi-Fi 字节计数增量，排除已识别虚拟网卡。KB = 1024 字节；切换接口、暂停或计数回退后重建基线。
- 显卡：PDH GPU Engine 计数器，合并同一硬件引擎上的进程占用，取最忙引擎。
- 硬盘：PDH PhysicalDisk 空闲时间的补值，显示最忙物理盘活动率，含 I/O，不是容量占用。

系统未提供计数器或首个采样没有基线时显示 `—`。识别虚拟接口采用规则，无法保证覆盖所有驱动。

## 任务栏与 DPI

程序只挂载自己的子窗口，不修改 Explorer 二进制、不注入代码，也不依赖 TrafficMonitor。自动寻找不与现有控件交叠的空位；空间不足时隐藏文字，保留托盘入口。

任务栏窗口按父窗口 DPI 上下文创建，坐标和截屏采用物理像素。使用不透明命中区域，约每 100ms 检查指针是否位于本窗口，停留约 450ms 显示提示；位置不变时不重复移动窗口，避免提示被打断。

全选布局为 598×40 逻辑像素，列宽依次为 76／72／72／86／60／100／100，列间隔为 4。关闭项目后自动组合和收缩。超长内容可能省略，悬停保留完整值。
