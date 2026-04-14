# KugouMusic.NET

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-brightgreen.svg)](https://github.com/Linsxyx/KugouMusic.NET/releases)

基于 **.NET 10.0 + Avalonia** 开发的**跨平台酷狗音乐桌面客户端**。

---

## ✨ 特性

- 🎵 完整集成酷狗音乐 API（搜索、歌单、每日推荐、歌手页、滚动歌词）
- 🔄 **在线自动更新**
- 🖥️ 真正跨平台（Windows / Linux / macOS）
- 🎨 现代 Avalonia UI + MVVM

---

## 📸 截图

![歌词界面](docs/images/lyrics.png)
![主界面](docs/images/main.png)
![搜索页面](docs/images/search.png)
![排行榜页面](docs/images/rank.png)
![发现页面](docs/images/tag.png)
---

## 🚀 下载与安装

### 推荐方式
访问 [Releases](https://github.com/Linsxyx/KugouMusic.NET/releases) 页面下载：

- **Windows**：`KugouAvaloniaPlayer-win.exe`
- **Linux**：`KugouAvaloniaPlayer-linux.AppImage`
- **Mac**：`KugouAvaloniaPlayer-mac.pkg`

安装后**每次启动程序会自动检查更新**，发现新版本会提示一键更新，可设置是否自动更新。

## 👍 灵感来自

[MakcRe/KuGouMusicApi](https://github.com/MakcRe/KuGouMusicApi)

---

## 📝 更新日志

### v0.9.3 
- 为 Windows 和 macOS 的歌词悬浮窗添加鼠标穿透功能
- 重构设置页面，并为设置页面添加选择歌词悬浮窗字体颜色功能

### v0.9.2
- 修复首次歌词加载时歌词挤在一起的问题
- 删除旧的滚动歌词样式

### v0.9.1
- 逐步实现 Apple Music 风格的滚动歌词

### v0.9.0
- 增加双击播放功能
- 为歌词显示增加逐字歌词
- 修复熔断提示无法自动消失
- 播放页面增加音量条



---

## 🛠️ 本地构建（开发者）

```bash
# 1. 克隆
git clone https://github.com/Linsxyx/KugouMusic.NET.git
cd KugouMusic.NET

# 2. 还原 & 构建
dotnet restore KugouMusic.NET.slnx
dotnet build KugouMusic.NET.slnx

# 3. 运行桌面客户端
dotnet run --project KugouAvaloniaPlayer/KugouAvaloniaPlayer.csproj

# 4. 调试酷狗API
dotnet run --project KgWebApi.Net/KgWebApi.Net.csproj
# 访问http://localhost:5058/scalar/v1
```