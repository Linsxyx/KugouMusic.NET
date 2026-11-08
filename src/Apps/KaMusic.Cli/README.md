# KaMusic.Cli 终端播放器

`KaMusic.Cli` 是仓库内的终端 TUI 播放器，自带差量渲染器与多协议终端图片引擎，使用独立的 `ApplicationData/kugou/KgTestSession.json` 持久化登录态。

## 运行

在仓库根目录执行：

```bash
dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj
```

可选诊断/预览命令：

```bash
# 运行二维码提取、图片协议编码与离屏 UI 自检
dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj -- --self-test

# 在当前终端直接预览本地或远程图片
dotnet run --project src/Apps/KaMusic.Cli/KaMusic.Cli.csproj -- --preview-image <path-or-url>
```

## 终端图片显示

播放器支持在终端内直接显示**登录二维码、用户头像、歌单封面、右侧正在播放封面、沉浸页大尺寸专辑封面**，并从封面提取主题强调色。

支持的图片协议（可在运行时按 `p` 或在设置页切换，也可通过环境变量 `KGTEST_IMAGE` 指定）：

| 模式 | 适用终端 | 说明 |
| --- | --- | --- |
| `Auto` | 自动检测 | 根据 `TERM` / `TERM_PROGRAM` / `KITTY_WINDOW_ID` / `WEZTERM_EXECUTABLE` 自动选择 |
| `Kitty` | Kitty、Ghostty、WezTerm | Kitty Graphics Protocol 高清位图 |
| `ITerm` | iTerm2、WezTerm | OSC 1337 内联高清位图 |
| `Sixel` | foot、mlterm、Windows Terminal 等 | Sixel 调色板位图 |
| `Blocks` | 任意支持 UTF-8 与 ANSI 颜色的终端 | `▀` 上下半块字符每格双像素采样；二维码则还原模块矩阵绘制，保证可扫码 |
| `Off` | 纯文本模式 | 关闭所有图片渲染 |

图片按终端字符格的真实像素比例缩放：Unix 终端通过 `ioctl(TIOCGWINSZ)` 读取窗口像素尺寸换算出字符格大小，
Kitty / iTerm2 协议再按该比例生成位图，避免图片被横向或纵向拉伸；纯字符块模式同样按真实比例排版。
Windows 终端无法探测时回退到常见的 1:2 字符格，可用 `KGTEST_CELL_SIZE=10x20` 手动指定。

颜色深度默认自动检测 24-bit TrueColor，在 `Apple_Terminal` 等终端自动降级为 xterm-256 色（可用 `KGTEST_COLOR=truecolor|256` 强制指定）。

## 页面

- `1` `每日推荐`
- `2` `私人 FM`
- `3` `发现歌单`
- `4` `排行榜`
- `5` `搜索`（同一页面按 `v` 切换歌曲结果与歌单结果）
- `6` `我的歌单`
- `7` `播放队列`
- `8` `设置`
- `9` `账号与登录`（内嵌扫码登录、短信验证码登录、60 秒倒计时与多账号选择）

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `↑` / `↓` 或 `j` / `k` | 移动选择（取决于当前焦点在导航栏还是内容区） |
| `PgUp` / `PgDn` / `Home` / `End` | 列表翻页 / 跳到首尾 |
| `Tab` / `←` / `→` | 在导航栏与内容区之间切换焦点；设置页中调整设置项；沉浸页中快退/快进 |
| `1` - `9` | 直接切换到对应页面 |
| `Enter` | 播放歌曲 / 打开歌单 / 触发按钮 |
| `Backspace` / `Esc` | 从歌单详情返回歌单列表 / 关闭弹窗 / 切换焦点 |
| `Space` | 播放 / 暂停 |
| `n` / `b` | 下一首 / 上一首 |
| `[` / `]` | 快退 / 快进 10 秒 |
| `+` / `-` | 调节音量；设置页内调节当前项或 EQ 增益 |
| `a` | 将当前选中歌曲加入“下一首播放” |
| `x` / `Delete` | 在播放队列页移除选中歌曲 |
| `v` | 在搜索页切换“歌曲”与“歌单”结果 |
| `/` | 打开非阻塞搜索弹窗 |
| `i` | 进入 / 退出沉浸式播放页（封面 + KRC 逐字高亮歌词 + 频谱） |
| `p` | 循环切换终端图片协议 |
| `s` / `t` / `l` | 随机播放 / 智能无缝过渡 / 歌词模式切换 |
| `d` | 私人 FM 中标记“不喜欢”并切到下一首 |
| `F5` / `F6` / `L` (`F9`) | 扫码登录 / 手机验证码登录 / 退出登录 |
| `r` | 刷新当前页面 |
| `?` | 打开快捷键帮助弹窗 |
| `q` | 返回上一层或退出程序 |
