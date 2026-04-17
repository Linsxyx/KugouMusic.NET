# KA Music

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-brightgreen.svg)](https://github.com/Linsxyx/KugouMusic.NET/releases)
[![Release](https://img.shields.io/github/v/release/Linsxyx/KugouMusic.NET)](https://github.com/Linsxyx/KugouMusic.NET/releases)

最好用、最轻量的酷狗音乐概念版播放器。

基于 **.NET 10 + Avalonia** 打造的跨平台桌面播放器，专注把酷狗音乐这件事做得更轻、更快、更像一个真正适合日常常驻的桌面应用。它不是浏览器壳，支持 Windows、Linux、macOS，也把本地播放、桌面歌词、托盘、热切音质、均衡器这些桌面用户真正会用到的体验补齐了。

> 注：“最好用、最轻量”为产品目标与设计方向。

## 快速入口

- 下载地址：<https://github.com/Linsxyx/KugouMusic.NET/releases>
- 问题反馈：<https://github.com/Linsxyx/KugouMusic.NET/issues>
- 桌面客户端：`KugouAvaloniaPlayer`
- 开发者入口：`KuGou.Net` / `KgWebApi.Net` / `SimpleAudio`

## 为什么值得用

- 更轻：基于 Avalonia 原生桌面栈，不走 Electron 路线
- 更像桌面播放器：托盘、桌面歌词、关闭行为、自动更新这些桌面交互是完整的
- 对 Linux 更友好，在linux也能拥有和其他平台一样的歌词悬浮窗体验
- 酷狗生态能力完整：登录、推荐、榜单、歌单、搜索、歌词、VIP 状态都已接入
- 在线和本地都能打：支持在线播放，也支持本地文件夹导入、本地歌词和本地播放
- 播放控制更到位：播放队列、随机模式、热切音质、10 段均衡器都可用

## 产品展示

### 主界面

每日推荐、歌单推荐、排行榜、搜索歌曲。

![主界面](docs/images/main.gif)

### 搜索与发现

搜索歌曲、歌单、专辑。

![搜索页面](docs/images/search.gif)

### 桌面歌词

Apple Music风格的滚动歌词，支持在线歌词和本地歌词，歌词浮窗全平台体验一致。

![歌词界面](docs/images/lyrics.png)

### 设置与播放体验

播放器设置、音效与常用行为都有独立入口。

![设置页面](docs/images/Setting.gif)

## 下载与平台支持

请前往 [Releases](https://github.com/Linsxyx/KugouMusic.NET/releases) 下载最新版本。

- Windows：`KugouAvaloniaPlayer-win.exe`
- Linux：`KugouAvaloniaPlayer-linux.AppImage`
- macOS 安装包：`KugouAvaloniaPlayer-mac-Setup.pkg`
- macOS 便携包：`KugouAvaloniaPlayer-mac.app.zip`

### 自动更新

项目通过 **Velopack + GitHub Releases** 提供更新能力。

- 使用安装包安装后：可在应用内检查更新
- 可在设置中启用“启动时自动检查更新”
- 直接运行便携包或本地 `dotnet run` 时：自动更新会被跳过

### macOS 说明

当前 macOS 安装包未签名，如遇到 Gatekeeper 拦截，可执行：

```bash
xattr -dr com.apple.quarantine /Applications/KugouAvaloniaPlayer.app
```

如果 `KugouAvaloniaPlayer-mac-Setup.pkg` 无法安装，也可以使用 `KugouAvaloniaPlayer-mac.app.zip` 解压直接运行，但这种方式不支持自动更新。

## 核心功能

### 在线内容

- 手机号验证码登录
- 二维码扫码登录
- 登录持久化与自动恢复
- 每日推荐
- 发现歌单与推荐内容
- 排行榜与榜单歌曲分页
- 歌手详情页
- 搜索歌曲、歌单、专辑
- 用户信息与 VIP 状态展示

### 本地播放

- 导入本地音乐文件夹并播放
- 未登录可播放本地歌曲
- 支持本地 KRC / LRC / VTT 歌词
- 在线歌词与本地歌词协同工作

### 桌面体验

- 桌面歌词浮窗
- 歌词逐字高亮与进度动画
- 系统托盘菜单
- 关闭行为可配置：退出程序 / 最小化到托盘
- 自动更新

### 播放控制与音质

- 在线歌曲播放
- 播放队列管理
- 下一首播放 / 随机模式
- 热切播放音质
- 10 段均衡器，支持预设与自定义
- 收藏当前歌曲、在线歌单增删改

### 跨平台支持

- Windows、Linux、macOS 三端可运行
- Linux 提供 `AppImage`
- 桌面歌词鼠标穿透支持跨平台实现
- `SimpleAudio + ManagedBass` 提供统一播放能力

## 为什么它在 Linux 上更值得用

很多音乐播放器对于Linux的支持很差，无法拥有和其他平台一样的体验。`KA Music` 的目标不是停留在能运行，而是尽量把 Linux 桌面用户真正会在意的体验补齐：发布产物、托盘、桌面歌词、本地播放、基础设置和日常交互都要可用。

## 常见问题

### 1. 为什么我本地运行时提示无法自动更新？

自动更新依赖 Velopack 安装。若你通过 `dotnet run` 或手动复制文件运行，应用会跳过自动更新检查。请使用 Releases 安装包安装。

### 2. 为什么未登录时不能播放在线歌曲？

当前在线播放要求有效登录态。你可以先登录，也可以直接播放本地导入的歌曲。

### 3. 为什么应用显示名是 KA Music，但安装包名还是 KugouAvaloniaPlayer？

这是为了兼容历史版本升级链路。应用显示名可以继续优化，但安装包标识保持稳定，能避免老版本升级断链。

### 4. 支持哪些歌词格式？

当前支持在线 KRC，以及本地 `KRC`、`LRC`、`VTT` 歌词。

## 开发者入口

这个仓库不只有播放器本体，也包含围绕酷狗能力做的几个底层项目。

- `KugouAvaloniaPlayer`：Avalonia 桌面客户端，也就是现在的 `KA Music`
- `KuGou.Net`：酷狗业务 SDK，封装登录、搜索、歌单、歌词、榜单、用户等能力
- `KgWebApi.Net`：基于 SDK 的 ASP.NET Core Web API 封装
- `SimpleAudio`：基于 ManagedBass 的跨平台播放与音效层
- `KuGou.Net.Native`：把 SDK 能力导出为 Native AOT 友好的 C ABI

### 本地开发

```bash
git clone https://github.com/Linsxyx/KugouMusic.NET.git
cd KugouMusic.NET

dotnet restore KugouMusic.NET.slnx
dotnet build KugouMusic.NET.slnx

dotnet run --project KugouAvaloniaPlayer/KugouAvaloniaPlayer.csproj
```

如需运行 Web API：

```bash
dotnet run --project KgWebApi.Net/KgWebApi.Net.csproj
```

Web API 文档（开发环境）：`http://localhost:5058/scalar/v1`

## 仓库结构

```text
KugouMusic.NET
├─ KugouAvaloniaPlayer   # Avalonia 桌面客户端
├─ KuGou.Net             # 核心 SDK
├─ SimpleAudio           # 音频播放与音效层
├─ KgWebApi.Net          # ASP.NET Core Web API
├─ KuGou.Net.Native      # Native AOT 导出层
└─ docs/images           # README 截图资源
```

## 更新日志

完整版本历史请查看 [Releases](https://github.com/Linsxyx/KugouMusic.NET/releases)。

### v1.0.0

- 正式发布 `KA Music` 1.0 版本
- 设置页重构为标准双栏布局
- 搜索页新增初始空页面、热搜标签快捷搜索、清空回到初始页
- 播放队列播放行为统一，修复页面切换后双击导致队列被重建的问题

### v0.9.6

- 新增解析 QQ 音乐歌单导入
- 新增桌面歌词悬浮窗字体自定义能力

## 免责声明与致谢

- 本项目仅用于技术学习与交流，请勿用于任何侵犯版权或违反服务条款的用途。
- 灵感来源：[MakcRe/KuGouMusicApi](https://github.com/MakcRe/KuGouMusicApi)
