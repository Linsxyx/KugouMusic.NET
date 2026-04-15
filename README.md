# KugouMusic.NET

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-brightgreen.svg)](https://github.com/Linsxyx/KugouMusic.NET/releases)
[![Release](https://img.shields.io/github/v/release/Linsxyx/KugouMusic.NET)](https://github.com/Linsxyx/KugouMusic.NET/releases)

基于 **.NET 10 + Avalonia** 的跨平台酷狗音乐生态项目，包含桌面客户端、SDK、Web API 与 Native 导出层。

## 项目简介

本仓库围绕酷狗音乐能力提供三类对外入口：桌面用户入口、SDK 开发入口、REST API 入口。

- `KugouAvaloniaPlayer`：跨平台桌面音乐播放器（Avalonia + MVVM）
- `KuGou.Net`：酷狗业务 SDK（登录、搜索、歌单、歌词、榜单、用户等）
- `KgWebApi.Net`：基于 SDK 的 ASP.NET Core Web API 封装
- `SimpleAudio`：基于 ManagedBass 的跨平台音频播放与音效层
- `KuGou.Net.Native`：将 SDK 能力导出为 Native AOT 友好的 C ABI

## 功能总览

| 领域    | 已实现能力                                                    | 入口 | 状态 |
|-------|----------------------------------------------------------|---|---|
| 登录    | 手机验证码登录、二维码登录、登录持久化                                      | 桌面端 / SDK / Web API | ✅ |
| 音乐内容  | 每日推荐、发现歌单、排行榜、歌手页、搜索（歌曲/歌单/专辑）                           | 桌面端 / SDK / Web API | ✅ |
| 播放与音效 | 在线播放、本地文件夹导入、播放队列、随机播放、下一首插队、均衡器                         | 桌面端 / SimpleAudio | ✅ |
| 歌词与交互 | 在线 KRC、本地 KRC/LRC/VTT、逐字高亮、桌面歌词浮窗、托盘控制、自动更新、网易云歌单导入、全屏播放 | 桌面端 | ✅ |

## 截图

![歌词界面](docs/images/lyrics.png)
![主界面](docs/images/main.png)
![搜索页面](docs/images/search.png)
![排行榜页面](docs/images/rank.png)
![发现页面](docs/images/tag.png)

## 快速开始（普通用户）

### 下载

请前往 [Releases](https://github.com/Linsxyx/KugouMusic.NET/releases) 下载最新包：

- Windows：`KugouAvaloniaPlayer-win.exe`
- Linux：`KugouAvaloniaPlayer-linux.AppImage`
- macOS（Velopack 安装包）：`KugouAvaloniaPlayer-mac-Setup.pkg`
- macOS（便携包）：`KugouAvaloniaPlayer-mac.app.zip`

### 自动更新说明

项目通过 **Velopack + GitHub Releases** 提供更新能力。

- 使用 Velopack 安装包安装后：可在应用内检查更新
- 可在「用户中心」启用“启动时自动检查更新”
- 非安装包直接运行（如 `.app.zip`、本地 `dotnet run`）时，自动更新会被跳过

### macOS 使用说明（当前未签名）

1. 推荐优先下载 `KugouAvaloniaPlayer-mac-Setup.pkg`（支持 Velopack 自动更新）。
2. 如果安装或启动被 Gatekeeper 拦截，请先执行：

```bash
xattr -dr com.apple.quarantine /Applications/KugouAvaloniaPlayer.app
```

3. 如果安装或启动 `KugouAvaloniaPlayer-mac-Setup.pkg` 失败，也可以下载 `KugouAvaloniaPlayer-mac.app.zip` 解压直接运行（这种方式不支持自动更新）。
   解压后可双击运行：`KugouAvaloniaPlayer-mac.app/KugouAvaloniaPlayer.app`。
   若从终端启动，请运行：`./KugouAvaloniaPlayer-mac.app/KugouAvaloniaPlayer.app/Contents/MacOS/KugouAvaloniaPlayer`。

## 快速开始（开发者）

```bash
# 1) 克隆
git clone https://github.com/Linsxyx/KugouMusic.NET.git
cd KugouMusic.NET

# 2) 还原与构建
dotnet restore KugouMusic.NET.slnx
dotnet build KugouMusic.NET.slnx

# 3) 运行桌面客户端
dotnet run --project KugouAvaloniaPlayer/KugouAvaloniaPlayer.csproj

# 4) 运行 Web API
dotnet run --project KgWebApi.Net/KgWebApi.Net.csproj
```

Web API 文档（开发环境）：`http://localhost:5058/scalar/v1`

## 桌面端能力清单

### 登录与账号

- 手机号验证码登录
- 二维码扫码登录（轮询状态）
- 登录态本地持久化与自动恢复
- 用户信息、VIP 状态展示与退出登录

### 音乐浏览与发现

- 每日推荐
- 发现页（歌单标签与推荐歌单）
- 排行榜与榜单歌曲分页加载
- 歌手详情页与歌曲列表
- 搜索：歌曲 / 歌单 / 专辑，支持详情页继续播放

### 播放与歌单管理

- 在线歌曲播放
- 导入本地音乐文件夹并播放
- 播放队列管理：清空、移除、下一首播放、随机模式
- 收藏当前歌曲、移除歌单歌曲
- 在线歌单新建、删除、收藏
- 网易云歌单链接解析并导入到酷狗歌单

### 歌词、音效与交互

- 歌词：在线 KRC + 本地 KRC/LRC/VTT
- 逐行高亮与逐字进度动画
- 桌面歌词浮窗（可锁定）
- 鼠标穿透：Windows / macOS 支持
- 10 段均衡器（预设 + 自定义）
- 系统托盘菜单：显示主界面、上一首、播放/暂停、下一首、退出
- 关闭行为可配置：退出程序 / 最小化到托盘

## 开发者：KuGou.Net SDK

### DI 注册

```csharp
using KuGou.Net.Infrastructure;

builder.Services.AddKuGouSdk();
```

### 核心 Client

- `AuthClient`：验证码登录、二维码登录、刷新会话、登出
- `MusicClient`：搜索、播放链接、热搜、歌手相关
- `PlaylistClient`：歌单详情、标签、增删改、加歌删歌
- `DiscoveryClient`：推荐歌单、新歌、每日推荐
- `RankClient`：榜单与榜单歌曲
- `UserClient`：用户信息、歌单、听歌数据、VIP 任务
- `LyricClient`：歌词搜索与拉取
- `AlbumClient`：专辑歌曲
- `DeviceClient`：获取设备ID

### 最小调用示例

```csharp
using KuGou.Net.Clients;

public sealed class DemoService(MusicClient musicClient)
{
    public async Task RunAsync()
    {
        var songs = await musicClient.SearchAsync("周杰伦");
        var first = songs.FirstOrDefault();
        if (first is null) return;

        var playInfo = await musicClient.GetPlayInfoAsync(first.Hash, "320");
        Console.WriteLine(playInfo?.Status);
    }
}
```

## 开发者：KgWebApi.Net

### 启动

```bash
dotnet run --project KgWebApi.Net/KgWebApi.Net.csproj
```

### API 文档

- OpenAPI/Scalar（开发环境）：`/scalar/v1`

### 核心路由示例（按控制器分组）

- 登录与验证码
  - `POST /Captcha/sent`
  - `POST /Login/mobile`
  - `GET /Login/qrcode/key`
  - `GET /Login/qrcode/check`
  - `POST /Login/refresh`
- 搜索与播放
  - `GET /Search?keywords=...`
  - `GET /Search/special?keywords=...`
  - `GET /Search/album?keywords=...`
  - `GET /Search/playUrl?hash=...&quality=320`
  - `GET /Search/hot`
- 歌单
  - `GET /PlayList/detail?globalcollectionid=...`
  - `GET /PlayList/track/all?globalcollectionid=...`
  - `POST /PlayList/Create?name=...`
  - `POST /PlayList/tracks/add`
  - `POST /PlayList/tracks/del`
- 发现与榜单
  - `GET /Discovery/playlist/recommend`
  - `GET /Discovery/newsong`
  - `GET /Rank/list`
  - `GET /Rank/songs?rankid=...`
- 用户
  - `GET /User/Detail`
  - `GET /User/playlist`
  - `GET /Youth/day/vip`




## 项目结构

```text
KugouMusic.NET
├─ KugouAvaloniaPlayer   # Avalonia 桌面客户端
├─ KuGou.Net             # 核心 SDK
├─ SimpleAudio           # 音频播放与音效层
├─ KgWebApi.Net          # ASP.NET Core Web API
├─ KuGou.Net.Native      # Native AOT 导出层
└─ docs/images           # README 截图资源
```

## 常见问题（FAQ）

### 1. 为什么我本地运行时提示无法自动更新？

自动更新依赖 Velopack 安装上下文。若你通过 `dotnet run` 或手动复制文件运行，应用会跳过自动更新检查。请使用 Releases 安装包安装。

### 1.1 macOS 提示“已损坏”或双击无响应怎么办？

当前发布包未签名/未公证，macOS 可能会拦截。可先执行：

```bash
xattr -dr com.apple.quarantine /Applications/KugouAvaloniaPlayer.app
```

再重新启动应用。

### 2. 为什么未登录时不能播放在线歌曲？

当前播放器对在线播放要求有效登录态。请先在登录弹窗中完成验证码登录或二维码登录。

### 3. 桌面歌词浮窗“鼠标穿透”为什么在 Linux 不生效？

当前实现仅对 Windows/macOS 提供原生穿透支持。

## 更新日志

完整版本历史请查看 [Releases](https://github.com/Linsxyx/KugouMusic.NET/releases)。

### v0.9.5

- 新增桌面歌词悬浮窗字体自定义能力
- 将登录态持久化能力从 SDK 解耦，分别落在桌面端与 Web API 侧
- 调整 macOS 发布流程，补充 `.app` 并重新接入 Velopack 打包链路

### v0.9.4

- 支持导入网易云歌单
- 实现窗口全屏

### v0.9.3

- 为 Windows 和 macOS 的歌词悬浮窗添加鼠标穿透功能
- 重构设置页面，并新增歌词悬浮窗字体颜色设置
- 对获取“我喜欢”歌单时概率报错 `39013` 做错误处理


## 免责声明与致谢

- 本项目仅用于技术学习与交流，请勿用于任何侵犯版权或违反服务条款的用途。
- 灵感来源： [MakcRe/KuGouMusicApi](https://github.com/MakcRe/KuGouMusicApi)
