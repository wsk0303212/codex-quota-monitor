# Codex 额度监控应用图标

绿色 C 形额度环与黄色提醒段，深色圆角底。C 同时对应 Codex 和额度环，延续任务栏绿色文字、黄色低额度提示的配色。保持少量粗线条，适合 16/32 像素的托盘和任务栏。

通过内置 image_gen 工具生成 PNG 原图，透明圆角与外边距保留。make-icon.ps1 只进行 Windows 图标格式封装及标准尺寸缩放，未改变图案。生成原件继续保留，程序消费的资产已复制到本项目。

- app-icon.png：原图，1254×1254，透明外边缘。
- app-icon.ico：16/20/24/32/40/48/64/128/256 像素的 Windows 图标。256 为 PNG 帧，其他帧为带透明通道的 32 位 DIB。
- icon-*.png：各个尺寸的预览。
- 程序 exe 包含 Win32 图标及嵌入图标资源；WPF 窗口用 256 像素帧，托盘用 16 像素帧。快捷方式直接指向本项目 ico。

生成提示词（内置工具模式）：

> Use case: logo-brand. Asset type: production Windows desktop application icon for a personal Codex usage quota monitor, one single standalone icon. Design a polished very simple geometric quota gauge: a substantial bright emerald green C-shaped circular ring, with just one short amber yellow arc at the lower-right endpoint communicating a low-quota warning. Place this bold gauge centrally within a nearly black charcoal rounded-square tile. The gauge should read like both a C and a remaining-capacity meter. Precisely centered, large emblem, broad smooth strokes, generous but restrained interior negative space. Flat crisp vector-like edges, no texture, no neon bloom, no gradients, no cast shadows, no 3D. The green ring and tiny amber arc are the entire symbol; no letters, text, numerals, clock hands, extra glyphs or official OpenAI knot. Rounded dark tile fills most of a square canvas, with a slim truly transparent margin and transparent outside rounded corners. Designed to remain identifiable at 16x16 and 32x32 pixels, so use only these few bold shapes. Output a clean high-resolution transparent PNG icon asset, no mockup, no presentation sheet, no multiple options, no watermark.

构建图标：用 Windows PowerShell 执行 tools/make-icon.ps1 -SourceImage <原图路径>。既有 ICO 已提交到本项目资产目录，正常构建不需要重新生成。
