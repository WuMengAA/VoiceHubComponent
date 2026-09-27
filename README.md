# VoiceHubComponent | 广播站排期插件

这是一个用于展示[VoiceHub](https://github.com/laoshuikaixue/VoiceHub)广播站点歌系统排期歌曲的ClassIsland插件，支持2.x版本。

使用建议：搭配滚动组件，开启宽度限制

## 功能

- 展示 VoiceHub 当天广播站排期歌曲。
- 支持按排期顺序和固定开始时间同步显示当前歌词行。
- 支持软件中途打开后按墙钟时间自动定位当前歌曲和当前歌词。
- 左侧固定「正在播放」：歌曲封面（交叉淡入切换）+ 歌名/歌手；右侧歌词：逐行上滑淡入切换、整行常亮、翻译/罗马音后缀、TTML 背景声/对唱行。
- 歌词格式支持 TTML、QRC/YRC 逐字、增强 LRC 与普通 LRC（优先级 TTML > 逐字 > LRC）。
- 跨平台歌词升级（与 VoiceHub 主项目同逻辑）：本侧歌词非最高阶格式时，自动搜索对侧平台（网易云 ↔ QQ 音乐）的 TTML/逐字歌词，经过候选打分匹配与歌词内容对齐校验后才会采用。由设置中的「启用跨平台歌词升级」控制，关闭时保持原有获取流程。
- 歌词与封面本地缓存，重复启动不重复拉取。
- 教室大屏「正在播放」叠加层：从 VoiceHub 实时拉取服务端权威的正在播放状态（SSE 主通道 + 30 秒轮询兜底，断线指数退避重连），命中当前排期时以叠加层形式呈现。默认开启，出错静默降级、不干扰主刷新。契约与排障见 `AI-必读-广播正在播放-桥接提示词.md`。

## 设置

- API 地址、固定开始时间、网易云 Cookie、调试排期日期（同旧版）。
- 显示封面 / 显示翻译 / 显示罗马音：控制组件显示效果。
- 启用跨平台歌词升级：默认开启。
- 显示正在播放叠加层（`EnableNowPlaying`）：默认开启。

## 构建

```
dotnet build -c Release
```

产物为 `bin/Release/net8.0-windows/VoiceHubComponent.dll`，插件包在 `cipx/VoiceHubComponent.cipx`。
构建末步会用 PowerShell 给插件包生成 MD5 清单（`cipx/checksums.md`）；没有 PowerShell 7 的
机器会自动回退到系统自带的 Windows PowerShell 5.1，无需手工补跑（也可显式指定解释器：
`dotnet build -c Release -p:PowershellBinaryName=<path>`）。

---

Powered By LaoShui @ 2025 - 2026
