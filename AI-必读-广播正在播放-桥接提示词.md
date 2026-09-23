# 桥接提示词 —— 教室大屏显示「正在播放」

> 给在 `Stelarith-voicehub-component` 里干活的 AI / 人。
> 服务端（`D:\Stelarith\Stelarith-voicehub-fork`）已经把「谁在播、播到第几秒」做成了权威状态并对外暴露字段；
> 本工程要做的是**把它显示到教室大屏上**。开工前请先读 `AI-必读-二次开发说明.md`。

---

## 0. 一句话任务

在 `VoiceHubControl` 的**当日排期列表**上，标出音乐管理员正在播放的那一条，并显示实时进度。
没有人在播时，界面必须与现在**完全一致**（不能留空徽标、不能占位、不能改行高）。

---

## 1. 服务端已经给你什么

### 1.1 你已经在读的接口：`GET /api/songs/public`

返回的是 **JSON 数组**（不是对象），每个元素是一条排期。本次新增 2 个字段：

```json
{
  "id": 2,
  "playDate": "2026-09-18",
  "sequence": 1,
  "played": false,
  "isPlaying": true,             // 新增：这一条就是当前正在播放的排期
  "broadcastPosition": 42.5,     // 新增：取到响应那一刻已播到第几秒；未在播为 null
  "playTime": null,
  "song": {
    "id": 1,
    "title": "广播测试曲",
    "artist": "验证歌手",
    "cover": "https://example.com/cover.jpg",
    "durationSeconds": 200,      // 本来就有，总时长用它
    "musicPlatform": "netease",
    "musicId": "999001"
  }
}
```

- `isPlaying` / `broadcastPosition` 是**逐条**的，不要只看第一条。
- 排序仍然是 `playDate` + `sequence`，**没有变**。
- 老服务端没有这两个字段 → `isPlaying` 反序列化成 `false`、`broadcastPosition` 成 `null`，
  正好等于「没有广播」，天然向后兼容，不需要版本探测。

### 1.2 更及时的接口：`GET /api/music/broadcast`（公开，无需鉴权）

```json
{
  "broadcast": {
    "songId": 1,
    "scheduleId": 2,             // 命中的当日排期 id；排期对不上时为 null
    "playDate": "2026-09-18",
    "sequence": 1,
    "title": "广播测试曲",
    "artist": "验证歌手",
    "cover": "https://example.com/cover.jpg",
    "musicPlatform": "netease",
    "musicId": "999001",
    "duration": 200,
    "position": 42.51,           // 已按服务器时间外推到「响应那一刻」
    "isPlaying": true,
    "publisherName": "播放员甲",   // 播控人姓名，可显示；不要写进任何对外日志
    "updatedAt": 1789661210646
  },
  "serverTime": 1789661210650,
  "nextUp": {                    // 新增：连播队列里的下一首；无连播队列时为 null
    "songId": 7,
    "title": "下一首·课间曲",
    "artist": "验证歌手",
    "duration": 188
  },
  "listeners": 12,               // 新增：当前在线收听人数（按 SSE + 心跳去重；总开关关时 0）
  "baselineReleased": false     // 新增：基准播控人掉线释放后为 true，提示可接管
}
```

`broadcast` 为 `null` 表示当前没有广播（或管理员把播控总开关关了，效果一样）。

- `nextUp`：连播（播放单）已编排时才有；大屏可在「正在播放」旁边显示「接下来：<title>」。
- `listeners`：在线收听人数，纯展示字段，掉线/无广播时返回 `0`。
- `baselineReleased`：基准播控人超过失联阈值被自动释放后为 `true`；集控侧无需动作，仅作提示。

### 1.3 SSE 实时通道：`GET /api/music/websocket`（公开）

`text/event-stream`，连接建立**立刻**下发一次快照，之后状态变化时推送：

| `type` | 说明 |
| --- | --- |
| `connection_established` | 建连成功，忽略即可 |
| `broadcast_state` | **要的就是它**，`data` 就是上面那个 `broadcast` 对象（null 表示停播）；同一个对象上挂着 `nextUp` / `listeners` / `baselineReleased` |
| `music_state_update` / `song_change` / `playlist_update` | 旧版 HarmonyOS 通道的消息，**忽略** |

### 1.3b 连播队列与播控权（仅后台/播控端用，大屏只读即可）

| 接口 | 说明 |
| --- | --- |
| `GET /api/music/broadcast/queue` | 当前播放单（连播队列：`items[]` 含 `songId/title/artist/duration`，`currentIndex` 指向在播项） |
| `POST /api/music/broadcast/queue` | 保存播放单（**播控行为**，需命中基准播控人，学生端不会调） |
| `POST /api/music/broadcast/listeners` | 学生端收听心跳上报（**公开**，用于统计 `listeners`；带防刷，别高频打） |
| `GET/POST /api/music/broadcast/authority` | 读取 / 修改基准播控人与总开关；`POST` 带 `action: claim|release` 可一键接管/释放 |

大屏与集控侧**只需要读** `1.1 / 1.2 / 1.3` 三个通道，无需关心队列写与接管。

---

## 2. ⚠️ 关键陷阱：本插件现在 1 小时才刷一次

`VoiceHubControl.axaml.cs` 的 `UpdateTimerInterval()` 常态把 `_refreshTimer.Interval` 设成
`TimeSpan.FromHours(1)`。如果「正在播放」只挂在主刷新上，大屏最多会**晚一小时**才亮起来 —— 不可接受。

正确做法：**另起一条独立通道专拿播控状态**，主刷新节奏不要动。

- 推荐：SSE（`/api/music/websocket`）为主 + **30 秒**轮询 `/api/music/broadcast` 兜底（SSE 断了能自愈）。
- 不要为了这个把 `/api/songs/public` 拉勤：那是整包排期+歌词，会把 VoiceHub 打爆。
- 进度条**本地 tick**，不要每秒发请求：

```csharp
// 收到快照时记锚点，之后每秒本地外推
private double _anchorPosition;
private DateTime _anchorAt;

private void ApplyBroadcast(BroadcastState? state)
{
    _state = state;
    _anchorPosition = state?.Position ?? 0;
    _anchorAt = DateTime.UtcNow;
}

private double CurrentPosition => _state?.IsPlaying == true
    ? _anchorPosition + (DateTime.UtcNow - _anchorAt).TotalSeconds
    : _anchorPosition;
```

- 服务端的 `position` 也会自己外推，所以偶尔收到一次新快照就能把漂移抹平，不用做时间校准。

---

## 3. 要改的文件

| 文件 | 改动 |
| --- | --- |
| `Models/VoiceHubModels.cs` | `SongItem` 加 `IsPlaying` / `BroadcastPosition`；新增 `BroadcastState` 模型 |
| 新增 `Services/BroadcastStateClient.cs` | SSE 订阅 + 兜底轮询 + 断线重连（**放后台线程，别阻塞 UI 线程**） |
| `VoiceHubControl.axaml` + `.axaml.cs` | 徽标 + 进度条渲染、缓存上一次状态、停播时清除 |
| `Models/VoiceHubSettings.cs` + `Views/VoiceHubSettingsView.axaml(.cs)` | 可选：`EnableNowPlaying` 开关、轮询间隔 |

### 3.1 模型（照抄 `SongItem` 的既有写法）

```csharp
[JsonPropertyName("isPlaying")]
public bool IsPlaying { get; set; }

/// <summary>
/// 广播中的播放进度（秒）；未在播为 null
/// </summary>
[JsonPropertyName("broadcastPosition")]
public double? BroadcastPosition { get; set; }

public class BroadcastState
{
    [JsonPropertyName("songId")] public int SongId { get; set; }
    [JsonPropertyName("scheduleId")] public int? ScheduleId { get; set; }
    [JsonPropertyName("playDate")] public string? PlayDate { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("position")] public double Position { get; set; }
    [JsonPropertyName("isPlaying")] public bool IsPlaying { get; set; }
    [JsonPropertyName("publisherName")] public string? PublisherName { get; set; }
    // 新版服务端可能多返回字段，JsonSerializer 默认忽略未知字段，无需处理
}

// 连播队列里的「下一首」摘要（快照的 nextUp；无连播时为 null）
public class BroadcastNextUp
{
    [JsonPropertyName("songId")] public int SongId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
}
```

快照整体（`GET /api/music/broadcast` 的 `broadcast` 字段 + 同级字段）建议单独包一层：

```csharp
public class BroadcastSnapshot
{
    [JsonPropertyName("broadcast")] public BroadcastState? Broadcast { get; set; }
    [JsonPropertyName("nextUp")] public BroadcastNextUp? NextUp { get; set; }
    [JsonPropertyName("listeners")] public int Listeners { get; set; }
    [JsonPropertyName("baselineReleased")] public bool BaselineReleased { get; set; }
    [JsonPropertyName("serverTime")] public long ServerTime { get; set; }
}
```

### 3.2 选择「哪一条在播」

优先按 `scheduleId` 精确匹配（同一天同一首歌可能被排两次）：

1. 排期项的 `id` == 快照的 `scheduleId` → 命中；
2. `scheduleId` 为 `null` 时，退化为 `songId` + `playDate` 都相同才命中；
3. 都匹配不上 → **什么都不标**（不要随便挑一条）。

---

## 4. 显示要求

- **徽标**：在命中项上显示一个「正在播放」标记（建议用主题强调色 + 一个脉动圆点，视觉别抢过歌曲名）。
- **进度**：进度条放在该项底部或右侧，`CurrentPosition / duration`，每秒更新、`clamp` 到 `[0,1]`。
- **下一首**：快照 `nextUp != null` 时，在「正在播放」下或旁边显示「接下来：<title>」（来自 `BroadcastNextUp.Title`）；`null` 时不显示该行。
- **在线人数**：快照 `listeners > 0` 时，可显示「N 人在听」（小字，非必须）；`listeners == 0` 或停播时不显示。
- **暂停态**：快照 `isPlaying == false` 时保留标记但**停止脉动**、进度条冻在 `position`，不要隐藏（否则大屏会闪）。
- **停播/掉线**：`broadcast_state` 收到 `null`，或本地判定快照过期（`isPlaying` 且超过 **95 秒**没有新快照）→ 整个标记与进度条**一起**移除，回到与现在完全一致的界面。
- **非当日排期**：组件在「今天没有排期」时会展示最近一条未来排期；此时**不要**叠加任何「正在播放」标记（广播只对当日有效）。
- **高对比/低配教室机**：不要为此引入动画库或 WebView，用现有 Avalonia 能力。

---

## 5. 容错（重要，教室机网络很差）

- 新接口 404/403/超时 → 当「没有广播」处理，**绝不影响排期与歌词的既有显示**。
- SSE 断开 → 指数退避重连（1s → 2s → 4s，封顶 30s），期间靠轮询兜底。
- 轮询失败 → 只记日志，不进 `ComponentState.NetworkError`（那是主刷新的状态机，别串台）。
- 并发：快照解析失败时保留上一次有效状态，不要清空（避免大屏一闪一闪）。

---

## 6. 🔴 红线：数据契约

本工程与集控侧（`D:\Stelarith\Stelarith-cims-eval`）**不共享代码，只共享数据契约**。

- 只**新增**读取字段，**不要改任何既有字段名/路径**（`id`、`playDate`、`sequence`、`song.*` 一律别动）。
- 本次新增的读取字段：`/api/songs/public` 的逐条 `isPlaying`/`broadcastPosition`；`/api/music/broadcast` 的 `nextUp`/`listeners`/`baselineReleased`；`/api/open/songs`、`/api/open/schedules` 的 `nowPlaying` 与逐条 `isPlaying`。**全部为新增，不破坏旧契约**。
- 一旦动了 `/api/songs/public` 的路径或字段名，必须同步检查集控侧的
  `StelarithSongBoard.cs` 与 `ext/voicehub-sync/voicehub-adapter.mjs`。
- 集控侧读的是 CIMS `Components/songboard`（优先）和 `/api/open/songs`（回退，需 `songs:read` 的 `x-api-key`）；
  这两处服务端也已补上 `nowPlaying` 与逐条 `isPlaying`，所以**两边可以显示同一件事**，但实现各自独立。
- `POST /api/music/broadcast/queue`、`POST /api/music/broadcast/authority`（含 `claim`）、`POST /api/music/broadcast/listeners` 为**写/播控接口**，大屏与集控侧**只读**，不要在本工程调用。

---

## 7. 本地怎么验（起一个真的在播的 VoiceHub）

服务端已有本地验证环境，直接用它对着写：

```bash
# 1) 起服务（生产构建产物，端口 3007；数据库 voicehub_dev 已建好并迁移）
cd D:/Stelarith/Stelarith-voicehub-fork
NODE_ENV=production PORT=3007 NITRO_PORT=3007 node .output/server/index.mjs

# 2) 让一个音乐管理员开播（账号 dj / Test1234!）
curl -s -c /tmp/cj -X POST http://127.0.0.1:3007/api/auth/login \
  -H 'content-type: application/json' \
  -d '{"username":"dj","password":"Test1234!"}'
curl -s -b /tmp/cj -X POST http://127.0.0.1:3007/api/music/broadcast \
  -H 'content-type: application/json' \
  -d '{"songId":1,"title":"广播测试曲","artist":"验证歌手","duration":200,"position":42,"isPlaying":true}'

# 3) 看组件要读的两个接口
curl -s http://127.0.0.1:3007/api/songs/public
curl -s http://127.0.0.1:3007/api/music/broadcast

# 4) 停播
curl -s -b /tmp/cj -X POST http://127.0.0.1:3007/api/music/broadcast \
  -H 'content-type: application/json' -d '{"action":"stop"}'
```

验收清单：

- [ ] 有人在播 → 命中排期出现徽标 + 进度条；进度每秒在走
- [ ] 停播 → 标记消失，界面回到改动前
- [ ] 老服务端（不含新字段）→ 界面与改动前一致，无异常日志
- [ ] 拔网线 30 秒 → 不崩、不复位排期、网络恢复后自动恢复
- [ ] 播控人把歌曲暂停 → 标记保留但不脉动
- [ ] 教室机 CPU 没有因新通道明显上升（不要 1 秒一次请求）

---

## 8. 不要做的事

- 不要改 `/api/songs/public` 的调用方式（它返回的是**数组**，别改成对象）。
- 不要往 VoiceHub 写任何数据；本插件是**只读**的。
- 不要为了新功能升级 `ClassIsland.PluginSdk` 或引入新的 NuGet 依赖。
- 不要把「正在播放」做成插件配置里的必填项——默认开启、出错静默降级。
- 不要用 `publisherName` 做任何鉴权或权限判断，它只是显示用的字符串。

---

## 9. 相关文档

- 本工程约定：`AI-必读-二次开发说明.md`
- 服务端改动（本次）：`D:\Stelarith\Stelarith-voicehub-fork` 的 `README.md` 项目结构、
  `server/utils/broadcast-state.ts`、`server/utils/broadcast-authority.ts`、
  `server/api/music/broadcast*.ts`
- 播控基准与权限模型：见服务端 `server/api/music/broadcast/authority.get.ts` 的注释
