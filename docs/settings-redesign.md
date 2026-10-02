# 设置项迁移清单

保留既有绑定与平台条件。EQ 改为页面导航；其他行为变更见下方说明。

| 新分组 | 选项/入口 | 原绑定与命令 | 平台条件 |
|---|---|---|---|
| 外观 | 主题模式 | IsDarkMode | Always |
| 外观 | 全局默认字体 | AppFontFamilyOptions, SelectedAppFontFamily, . | Always |
| 外观 | 自定义背景图 | CustomBackgroundImageStatus, UseCustomBackgroundImage, PickCustomBackgroundImageCommand, ClearCustomBackgroundImageCommand | Always |
| 外观 | 图片透明度 | CustomBackgroundImageOpacityDisplay, CustomBackgroundImageOpacity | Always |
| 外观 | 播放页背景来源 | NowPlayingBackgroundSourceOptions, SelectedNowPlayingBackgroundSource, Converter | Always |
| 外观 | 播放页背景模糊度 | NowPlayingBackgroundBlurRadiusDisplay | Always |
| 播放与音效 | 输出设备 | IsOutputDeviceSelectionVisible, OutputDeviceStatus, IsOutputDeviceStatusVisible, OutputDeviceOptions, SelectedOutputDevice, Name, RefreshOutputDevicesCommand | Output |
| 播放与音效 | 默认播放音质 | Player.QualityOptions, Player.QualitySelection, . | Always |
| 播放与音效 | 音效预设 | EQPresetOptions, SelectedEQPreset, OpenEqSettingsCommand | Always |
| 播放与音效 | 智能过渡 | EnableSeamlessTransition | Always |
| 播放与音效 | 自动音量平衡 | EnableVolumeNormalization | Always |
| 播放与音效 | 空间音效 | EnableSurround | Always |
| 歌词与播放画面 / 桌面 | 歌词锁定效果 | UnlockDesktopLyricCommand | Always |
| 歌词与播放画面 / 桌面 | 排版方向 | DesktopLyricLayoutOptions, DesktopSelectedLyricLayout | Always |
| 歌词与播放画面 / 桌面 | 双行歌词 | DesktopLyricDoubleLineEnabled | Always |
| 歌词与播放画面 / 桌面 | 对齐方式 | LyricAlignmentOptions, DesktopSelectedLyricAlignment | Always |
| 歌词与播放画面 / 桌面 | 颜色应用目标 | LyricColorTargetOptions, DesktopSelectedLyricColorTarget | Always |
| 歌词与播放画面 / 桌面 | 颜色模式 | LyricColorModeOptions, DesktopSelectedLyricColorMode | Always |
| 歌词与播放画面 / 桌面 | 字体模式 | LyricFontModeOptions, DesktopSelectedLyricFontMode | Always |
| 歌词与播放画面 / 播放页 | 播放页音频可视化 | EnableNowPlayingVisualizer | Always |
| 歌词与播放画面 / 播放页 | 颜色应用目标 | LyricColorTargetOptions, PlayPageSelectedLyricColorTarget | Always |
| 歌词与播放画面 / 播放页 | 颜色模式 | LyricColorModeOptions, PlayPageSelectedLyricColorMode | Always |
| 歌词与播放画面 / 播放页 | 字体模式 | LyricFontModeOptions, PlayPageSelectedLyricFontMode | Always |
| 歌词与播放画面 / 播放页 | 关闭上升动效 | UseLightweightNowPlayingLyricScroll | Always |
| 歌词与播放画面 / 播放页 | 歌词大小 | PlayPageLyricFontSizeDisplay | Always |
| 歌词与播放画面 / 播放页 | 歌词对齐 | LyricAlignmentOptions, PlayPageSelectedLyricAlignment | Always |
| 歌词与播放画面 / 任务栏 | 任务栏歌词 | ResetTaskbarLyricsAppearanceCommand | Taskbar |
| 歌词与播放画面 / 任务栏 | 启用任务栏歌词 | EnableTaskbarLyrics | Taskbar |
| 歌词与播放画面 / 任务栏 | 显示翻译 | TaskbarLyricsShowTranslation | Taskbar |
| 歌词与播放画面 / 任务栏 | 歌词对齐 | TaskbarLyricAlignmentOptions, TaskbarSelectedLyricAlignment | Taskbar |
| 歌词与播放画面 / 任务栏 | 水平微调 | TaskbarLyricsHorizontalOffset | Taskbar |
| 歌词与播放画面 / 任务栏 | 字体 | LyricFontFamilyOptions, TaskbarSelectedLyricFontFamily, . | Taskbar |
| 歌词与播放画面 / 任务栏 | 字号 | TaskbarLyricFontSizeOptions, TaskbarLyricsFontSize | Taskbar |
| 歌词与播放画面 / 任务栏 | 未播放颜色 | TaskbarUnplayedColorPreviewBrush, TaskbarUnplayedColorHexInput, ApplyTaskbarUnplayedColorHexCommand | Taskbar |
| 歌词与播放画面 / 任务栏 | 已播放颜色 | TaskbarPlayedColorPreviewBrush, TaskbarPlayedColorHexInput, ApplyTaskbarPlayedColorHexCommand | Taskbar |
| 快捷键 | 启用全局快捷键 | EnableGlobalShortcuts | Always |
| 快捷键 | 清空 | ShortcutItems, DisplayName, ShortcutText, ., StatusMessage, HasStatusMessage, StatusForeground | Always |
| 通用 | 关闭主面板时 | AvailableCloseBehaviors, SelectedCloseBehavior, Converter | Always |
| 通用 | Linux 系统窗口标题栏 | IsLinuxWindowDecorationToggleVisible, LinuxUseFullWindowDecorations | Linux |
| 通用 | 日志文件夹 | OpenLogFolderCommand | Always |
| 通用 | 一键重置 | ResetAllSettingsCommand | Always |
| 更新与关于 | QQ群：1081635731 (欢迎进来提建议或者交流反馈) | AppDisplayName, AppVersion, OpenRepositoryCommand | Always |
| 更新与关于 | 自动检查更新 | AutoCheckUpdate | Always |
| 更新与关于 | 手动检查更新 | CheckForUpdateCommand | Always |
| 更新与关于 | 最近更新日志 | RefreshReleaseNotesCommand | Always |
| 账户 | 退出登录 | LogoutCommand | Always |

## 交互约定

- 普通设置即时生效；颜色文本显式应用并就地校验。
- 使用分类导航查找设置；已按反馈移除搜索、独立字体/字样预览区和背景图片预览。
- 导航使用自定义选中模板，只为当前项显示液态玻璃底层；选中前后的文字均为 14 号、正常字重和常规文字颜色。悬停与键盘焦点分别反馈。
- EQ：对数横轴，自由笔画映射十段增益；预设共享，单步撤销；16ms 合并音频应用，250ms 合并保存，结束笔画和离开页面立即保存。
- 系统图片选择器及现有平台功能限制继续保留。

## 验证入口

- `dotnet build src/Apps/KugouAvaloniaPlayer/KugouAvaloniaPlayer.csproj -f net10.0`
- `dotnet build src/Apps/KugouAvaloniaPlayer/KugouAvaloniaPlayer.csproj -f net11.0`
- `dotnet test src/Tests/KugouAvaloniaPlayer.Tests/KugouAvaloniaPlayer.Tests.csproj`
- 真机检查：800×600、1000×700、1440×900，深浅主题；分类选中、悬停和键盘焦点；进入 EQ、往返绘制、撤销、返回再进入；重置和退出确认的取消、Esc 和焦点恢复；断网更新检查。
- 绘制验证必须包含真实播放、切歌与重启恢复；编译和轨迹单元测试不能代替这部分验收。

## 首轮验证记录（2026-10-02，后续精简前）

- net10.0、net11.0 播放器构建均通过，0 警告、0 错误。
- 14 项行为测试在两个目标框架均通过，覆盖轨迹映射、反向与局部绘制、增益限制、撤销、预设、搜索以及更新源全部失败的保护。
- 原页面 109 个业务绑定全部保留；8 个导航/平台绑定由新外壳接管。46 个搜索目标均对应现有命名控件。
- 真实 1000×700 窗口已打开设置，切换至播放与音效，并进入独立 EQ 页。
- 此后桌面锁定，未能完成最终样式复查、多尺寸/深浅主题验收、手势试听、切歌与重启恢复验证。这些仍需真机验证。

## 精简调整验证（2026-10-02）

- 移除设置搜索及其索引、跳转和高亮逻辑，同时移除独立字体/字样预览区。
- 分类选中态采用主题色竖线、浅底和细描边，分别处理悬停及键盘焦点。
- net10.0、net11.0 构建通过，均为 0 警告、0 错误；移除搜索测试后，剩余 11 项测试在两个框架均通过。
- 真机窗口确认搜索与字体预览已移除，鼠标分类切换和方向键选择正常，新选中效果已显示。

## 导航与背景预览调整（2026-10-02）

- 设置导航自行控制模板和状态，替换继承的默认项主题，清除蓝色选中块。当前项只显示玻璃底层，文字保持正常字重与主题文字颜色。
- 真机自定义背景下确认蓝色选中块消失，方向键切换分类正常；用户确认效果。
- 删除背景图片预览区及对应图片解码、加载和状态属性；选图、清除、透明度与实际应用背景逻辑保持原样。

## 文本层级精简（2026-10-02）

- 页面保留一个分类标题；移除页头描述、品牌字样及重复分组标题。窄窗口以分类选择器代替重复页标题。
- 普通选项使用正常字重；删去仅复述控件的说明，保留功能限制、错误信息与重置影响。
- 快捷键仅在录制按钮上显示当前按键；更新状态移到检查按钮旁；背景图片只显示文件名，完整路径放在悬停提示中。
- 真机自定义背景下检查外观和快捷键页，标题及重复按键文字已精简。此次未重新验证所有窗口尺寸和平台专属页面。
