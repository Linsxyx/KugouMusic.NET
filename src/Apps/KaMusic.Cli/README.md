# KaMusic.Cli 终端播放器

`KaMusic.Cli` 是酷狗音乐概念版终端 TUI 播放器，自带差量渲染器与多协议终端图片引擎，支持沉浸式 KRC 逐字歌词、实时频谱可视化、智能无缝过渡与多账号登录。已发布至 npm（包名：`@linsxyx/kamusic`），全平台 NativeAOT 原生编译分发，无需预装 .NET 运行时。

## 安装与运行

### 方式一：通过 npm 安装（推荐，免 .NET 运行时）

支持 Windows (`x64`/`arm64`)、Linux (`x64`/`arm64`) 与 macOS (`x64`/`arm64`)：

```bash
# 全局安装
npm install -g @linsxyx/kamusic

# 启动播放器
kamusic

# 检查并一键更新到最新版本
kamusic update
```

或者无需安装直接运行：

```bash
npx @linsxyx/kamusic
```

### 方式二：源码编译运行（开发者，需 .NET 10 SDK）

在仓库根目录执行：

```bash
dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj
```

### 诊断与离屏预览命令

```bash
# 运行二维码提取、图片协议编码与离屏 UI 自检
kamusic --self-test
# 源码方式：dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj -- --self-test

# 在当前终端直接预览本地或远程图片
kamusic --preview-image <path-or-url>
# 源码方式：dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj -- --preview-image <path-or-url>
```

## 系统依赖说明 (Linux)

- **音频输出**：基于 BASS 音频库，Linux 环境需具备 ALSA / PulseAudio / PipeWire 库支持（大多数桌面发行版默认自带，若缺少可安装 `libasound2` 或 `pulseaudio`）。
- **字体与编码**：建议终端使用支持 Unicode 符号的 Nerd Font 字体，并开启 UTF-8 编码。

## 终端图片显示

播放器支持在终端内直接显示**登录二维码、用户头像、歌单封面、右侧正在播放封面、沉浸页大尺寸专辑封面**，并从封面提取主题强调色。

支持的图片协议（可在运行时按 `p` 或在设置页切换，也可通过环境变量 `KGTEST_IMAGE` 指定）：

| 模式 | 适用终端 | 说明 |
| --- | --- | --- |
| `Auto` | 自动检测 | 根据 `TERM` / `TERM_PROGRAM` / `KITTY_WINDOW_ID` / `WEZTERM_EXECUTABLE` 自动选择 |
| `Kitty` | Kitty、Ghostty、WezTerm | Kitty Graphics Protocol 高清位图 |
| `ITerm` | iTerm2、WezTerm | OSC 1337 内联高清位图 |
| `Sixel` | foot、mlterm、Windows Terminal 等 | Sixel 调色板位图（可用 `KGTEST_CELL_SIZE=10x20` 微调字符格像素尺寸） |
| `Blocks` | 任意支持 UTF-8 与 ANSI 颜色的终端 | `▀` 上下半块字符每格双像素采样；二维码则还原模块矩阵绘制，保证可扫码 |
| `Off` | 纯文本模式 | 关闭所有图片渲染 |

颜色深度默认自动检测 24-bit TrueColor，在 `Apple_Terminal` 等终端自动降级为 xterm-256 色（可用 `KGTEST_COLOR=truecolor|256` 强制指定）。

## 页面

- `1` `每日推荐`
- `2` `私人 FM`
- `3` `探索发现`（整合官方推荐歌曲卡片、歌单广场与分类标签筛选、榜单精选与巅峰榜单墙，支持二维卡片网格与钻取）
- `4` `本地音乐`（与桌面客户端共享 `kugou-player.db` 本地曲库，支持导入本地音频目录/单曲、读取内嵌封面与歌词、免登录直接播放）
- `5` `搜索`（同时检索本地曲库与在线曲库，按 `v` 切换歌曲结果与歌单结果）
- `6` `我的歌单`
- `7` `播放队列`
- `8` `设置`
- `9` `账号与登录`（内嵌扫码登录、短信验证码登录、60 秒倒计时与多账号选择；未登录播放在线歌曲时自动弹窗引导登录）

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `↑` / `↓` / `←` / `→` (`h`/`j`/`k`/`l`) | 列表移动选择；探索发现页中二维移动卡片焦点 |
| `[` / `]` 与 `,` / `.` | 探索发现页切换子栏目（综合/专属推荐/歌单广场/巅峰榜单）与歌单标签；其他页面快退/快进 10 秒 |
| `PgUp` / `PgDn` / `Home` / `End` | 列表翻页 / 跳到首尾 |
| `Tab` | 在导航栏与内容区之间切换焦点 |
| `1` - `9` | 直接切换到对应页面 |
| `Enter` | 播放歌曲 / 打开探索卡片或歌单 / 触发按钮 |
| `Backspace` / `Esc` | 从歌单或卡片详情返回上一层 / 关闭弹窗 / 切换焦点 |
| `o` / `c` / `x`（本地音乐页） | 导入本地音频文件夹或单曲路径 / 新建本地歌单 / 删除本地歌单或移除曲目 |
| `Space` | 播放 / 暂停 |
| `n` / `b` | 下一首 / 上一首 |
| `+` / `-` | 调节音量；设置页内调节当前项或 EQ 增益 |
| `a` | 将当前选中歌曲加入“下一首播放” |
| `x` / `Delete` | 在播放队列页移除选中歌曲；在本地音乐页删除选中歌单或曲目 |
| `v` | 在搜索页切换“歌曲”与“歌单”结果 |
| `/` | 打开非阻塞搜索弹窗 |
| `i` | 进入 / 退出沉浸式播放页（封面 + KRC 逐字高亮歌词 + 频谱） |
| `p` | 循环切换终端图片协议 |
| `s` / `t` / `l` | 随机播放 / 智能无缝过渡 / 歌词模式切换 |
| `d` | 私人 FM 中标记“不喜欢”并切到下一首 |
| `F5` / `F6` / `L` (`F9`) | 扫码登录 / 手机验证码登录 / 退出登录 |
| `r` | 刷新当前页面（在本地音乐页同时重新扫描已导入文件夹） |
| `?` | 打开快捷键帮助弹窗 |
| `q` | 返回上一层或退出程序 |
