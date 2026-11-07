# 原生 Wayland 桌面歌词

Rust + GTK4 + gtk4-layer-shell 桌面歌词后端。Avalonia 保留播放、歌词解析和设置；此程序独立拥有 Wayland layer surface，通过标准输入/输出交换 JSON Lines。

## 构建和运行

需要 Rust 1.92+、GTK4 4.6+、gtk4-layer-shell 1.0+ 及其开发文件、pkg-config。例如 Arch Linux：

```bash
sudo pacman -S rust gtk4 gtk4-layer-shell pkgconf
bash src/Native/KugouWaylandLyrics/build.sh
dotnet run --project src/Apps/KugouAvaloniaPlayer
```

Ubuntu 24.04 可以安装 `libgtk-4-dev libwayland-dev wayland-protocols meson ninja-build pkg-config`，再按 `.github/workflows/release.yml` 中的步骤构建 gtk4-layer-shell。开发时如使用自定义安装前缀，设置 `PKG_CONFIG_PATH` 和 `LD_LIBRARY_PATH`。

脚本将可执行文件、layer-shell 动态库和许可证复制到播放器的 `Native/` 目录，随后 `dotnet build/publish` 将它们复制到输出目录。GTK4 及其图形依赖使用发行版版本；layer-shell 的小型动态库随程序分发。原生辅助程序使用相对 RPATH 查找 `wayland/libgtk4-layer-shell.so.0`。

原生后端默认关闭。Linux 下可在“设置 → 歌词 → 桌面”打开“使用原生桌面歌词”；开关会保存，当前已打开的歌词会立即切换窗口。仅在开关打开、存在 `WAYLAND_DISPLAY` 且辅助程序已打包时尝试原生后端。收到版本匹配的 `ready` 后启用；缺少运行库、不支持 layer-shell、启动超时等情况自动回退到 Avalonia。GNOME Wayland 没有 layer-shell，使用回退路径。设置 `KUGOU_DESKTOP_LYRICS_BACKEND=avalonia` 可强制使用原窗口。

## 已实现的行为

- 横排/竖排、逐字时间进度、翻译、双行、对齐、字体、字号和颜色设置。
- Pango/Cairo 绘制中文和文本描边。横排长句缩小到可用宽度；竖排按字素从上到下、从右到左排列。
- 锁定时正文与操作栏的背景、边框和模糊关闭，只保留透明歌词和一个 40×40 的圆形锁图标。锁图标悬停渐显，点击解锁；歌词区域及其余屏幕穿透。主界面设置中的“解除锁定”也可使用。
- 解锁后显示圆角玻璃面板及 GTK 原生图标操作栏，支持前后切歌、播放暂停、字号、翻译、操作栏位置、显示器切换、锁定和关闭。竖排操作栏分两行排列。
- 在歌词背景按住鼠标左键拖动调整位置，鼠标显示抓取状态。拖动使用固定的输出坐标系，点击按钮不触发拖动。已发出的旧播放状态不会覆盖尚未确认的解锁或位置更改。
- 当 GTK 支持 CSS `backdrop-filter` 且合成器支持 `ext-background-effect` 时，玻璃面板会请求真实背景模糊。锁定时显式清除模糊请求；较旧的 GTK 或不支持模糊的合成器保留半透明底色、圆角和细描边。
- 横排、竖排分别保存显示器连接器及显示器内的逻辑坐标。跨显示器使用“屏幕”按钮切换；不依赖全局鼠标坐标。
- 默认 `top` 层；设置中的“全屏应用上显示歌词”切换为 `overlay`。不申请键盘焦点或占用工作区。
- 主进程每 100ms 提供时间锚点，子进程使用单调时钟推进动画；暂停、跳转和变速随下一个状态校准。
- 慢消费者只保留最新状态；stdin 关闭时退出，即使显示器关闭、不再提供 frame callback，也会检测 EOF。

原生操作栏采用独立的玻璃样式和淡入淡出过渡，尚未复刻 Avalonia 的歌词换行动画。复杂双向文字的逐字视觉方向需要进一步适配。此后端不请求锁屏协议，无法覆盖会话锁屏。

以下为 GTK 原生控件快照，透明区域显示为查看器背景；合成器提供的桌面背景模糊不会包含在控件快照中：

![解锁后的原生歌词面板](../../../docs/images/wayland-native-lyrics-unlocked.png)

![锁定后的透明歌词与锁图标](../../../docs/images/wayland-native-lyrics-locked.png)

## 协议 v1

stdin 每行一个 UTF-8 JSON 对象，字段使用 snake_case。完整字段见 `src/protocol.rs` 的 `State` 与 C# `WaylandLyricProtocol.cs`。单条消息上限 1MiB。歌词及单词时间均为歌曲开始后的绝对毫秒；不是相对行时间。颜色为 C# 的 `#AARRGGBB`。

```json
{"version":1,"position_ms":1500,"playing":true,"speed":1,"current":{"text":"你好","start_ms":1000,"duration_ms":1000,"words":[{"text":"你","start_ms":1000,"duration_ms":500},{"text":"好","start_ms":1500,"duration_ms":500}]},"locked":true}
```

stdout 仅输出事件，日志写入 stderr：

```json
{"event":"ready","version":1}
{"event":"action","name":"unlock"}
{"event":"placement","vertical":false,"placement":{"has_value":true,"x":120,"y":600,"monitor":"DP-1"}}
```

启动握手在 surface 映射并应用输入区域后发送。运行中辅助程序退出会同步关闭状态；再次打开可重新启动。

## 验证

```bash
cargo test --locked --manifest-path src/Native/KugouWaylandLyrics/Cargo.toml
cargo clippy --locked --all-targets --manifest-path src/Native/KugouWaylandLyrics/Cargo.toml -- -D warnings
dotnet test src/Tests/KaMusic.Tests/KaMusic.Tests.csproj --filter FullyQualifiedName~WaylandLyricProtocolTests
```

实际 layer-shell 会话下运行以下冒烟验证（短暂显示测试歌词，不修改播放器设置）：

```bash
python3 src/Native/KugouWaylandLyrics/tests/smoke.py src/Apps/KugouAvaloniaPlayer/Native/kugou-wayland-lyrics
```

原生控件交互测试默认跳过，需在 layer-shell 会话中显式运行（使用真实 GTK 控件命中、按钮点击信号和拖动回调，不注入系统鼠标事件）：

```bash
GDK_BACKEND=wayland cargo test --locked --manifest-path src/Native/KugouWaylandLyrics/Cargo.toml \
  native_unlock_buttons_and_drag_use_real_widget_layout -- --ignored --nocapture
```

设置 `KUGOU_WAYLAND_PREVIEW_DIR` 可在该测试中导出锁定/解锁控件快照。冒烟测试还会检查解锁时提交了模糊区域，以及锁定后 blur region 已撤销。

真实指针事件回归测试通过 gtk4-layer-shell 的隔离模拟合成器发送 `wl_pointer.enter/button`，覆盖按下锁图标时的手势取消、解锁事件回传、旧状态回放及反复锁定/解锁，不会操作桌面的系统鼠标。发布 CI 也会运行此测试：

```bash
# 在 gtk4-layer-shell 源码的 Meson 构建目录中：
meson configure /path/to/layer-build -Dtests=true -Dsmoke-tests=false
meson compile -C /path/to/layer-build mock-server
python3 src/Native/KugouWaylandLyrics/tests/pointer.py \
  /path/to/layer-build/test/mock-server/mock-server \
  src/Apps/KugouAvaloniaPlayer/Native/kugou-wayland-lyrics
```

辅助程序的 stderr 与锁定/解锁事件以 Information 级别交给播放器日志；子进程异常退出会记录退出码。主程序在子进程退出时先读取完剩余事件，再释放歌词状态。

还需人工检查：锁定时点击歌词能操作下方应用；解锁热点不抢键盘焦点；拖动、不同缩放的多屏切换、热插拔、全屏应用、niri Overview，以及主程序退出后的子进程回收。
