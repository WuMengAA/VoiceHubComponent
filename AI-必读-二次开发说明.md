# ⚠️ AI 必读 —— `Stelarith-voicehub-component` 二次开发说明

> 本工程是 **VoiceHubComponent（ClassIsland 广播站排期插件）** 的本地 fork，
> 用于 Stelarith 集控体系的二次开发。开工前请先读完本文件。

## 1. 这是什么

- **上游官方**：<https://github.com/laoshuikaixue/VoiceHubComponent>
  （作者 LaoShui，`laoshui.voicehub`，展示 VoiceHub 广播站当日排期歌曲 + 高级歌词）
- **本工程**：`D:\Stelarith\Stelarith-voicehub-component`
- **自有仓库**：`git@github.com:WuMengAA/VoiceHubComponent.git`

## 2. 分支策略

| 分支 | 用途 |
| --- | --- |
| `main` | **我方开发主干**（独立于官方 main）。二次开发一律提交到这里 |
| `upstream/v2` | 官方 2.x 线（ClassIsland 2.x 适配），是 `main` 的**基线来源** |
| `upstream/main` | 官方 1.x 线，仅作参考，**不要**合并进来 |

> 为什么基线与官方 `main` 不同：本工程面向 ClassIsland **2.1.0.1**，
> 官方 `v2` 分支才是 2.x 适配线（`apiVersion: 2.0.0.0`）。
> 我方 `main` 从 `upstream/v2` 派生，之后独立演进。

## 3. 远端配置（已配好）

```
origin    git@github.com:WuMengAA/VoiceHubComponent.git      # 自有，推送用
upstream  https://github.com/laoshuikaixue/VoiceHubComponent.git  # 官方，只读拉取
```

推送走 **SSH**（不要用 HTTPS/PAT，见用户环境的既定结论）：

```bash
GIT_SSH_COMMAND="ssh -o StrictHostKeyChecking=no" git push origin main
```

## 4. 同步官方更新（定期）

```bash
git fetch upstream --tags
git log --oneline main..upstream/v2     # 看官方 v2 有哪些新提交
git diff main upstream/v2 --stat        # 看改了哪些文件
# 确认无误后合并（冲突通常集中在 VoiceHubControl.axaml.cs / Models）
git merge upstream/v2
```

**同步前务必先读上面的 diff**：官方改动可能与我们为集控做的定制（数据来源、显示字段）冲突。

## 5. 与 Stelarith 集控的关系

集控侧的 ClassIsland 插件是另一个工程：

`D:\Stelarith\Stelarith-cims-eval\school-multimedia-control\ext\stelarith-classisland-plugin`

两边**不共享代码**，只共享**数据契约**：

- 官方组件读 VoiceHub 的 `/api/songs/public`（排期表，含歌词升级）；
- 集控插件的「集控 · 正在播放 / 点歌名单」读 **CIMS `Components/songboard` 资源**（集控推送优先），
  拿不到时回退直连 VoiceHub 开放 API（`/api/open/songs`，需 `songs:read` 的 `x-api-key`）。

因此本工程改动如果动了 **API 路径或字段名**，必须同步检查集控侧的
`StelarithSongBoard.cs` 与 `ext/voicehub-sync/voicehub-adapter.mjs`。

## 6. 构建

```bash
dotnet build -c Release
```

- 目标框架 `net8.0-windows`，SDK 包 `ClassIsland.PluginSdk`（`<ClassIslandPluginSdkVersion>`，官方用 2.0.0.1）。
- 本机 ClassIsland 为 2.1.0.1，实测包在 `~/.nuget/packages/classisland.core/2.1.0.1/`。
- `CreateCipx=true` 会额外产出 `.cipx` 插件包。
- 部署：把 `VoiceHubComponent.dll` + `manifest.yml` + `icon.png` 放进
  `D:\Classlsland\data\Plugins\`（**数据目录**下的 Plugins，不是 `%APPDATA%`）。

## 7. 已知环境注意点

- `D:\Stellara\...` 旧路径**全部失效**，一律换成 `D:\Stelarith\...`。
- 官方 `VoiceHubSettings` 把配置写在 `%APPDATA%\ClassIsland\Plugins\VoiceHubComponent\settings.json`，
  而插件本体装在 `D:\Classlsland\data\Plugins\` —— 两者不是一个目录，排查配置问题时注意。
