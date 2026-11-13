using System.Net;
using System.Security.Cryptography;
using KaMusic.Cli.Graphics;
using KaMusic.Cli.Models;
using KaMusic.Cli.Services;
using KaMusic.Cli.Tui;
using KaMusic.Cli.Views;
using KuGou.Net.Protocol.Session;
using SkiaSharp;

namespace KaMusic.Cli;

// 命令行自检与预览：支持 --self-test 验证图片解码/二维码提取/全部协议编码/离屏画布渲染，--preview-image 在终端直接预览图片。

internal static class CliDiagnostics
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--preview-image", StringComparer.OrdinalIgnoreCase))
        {
            var idx = Array.FindIndex(args, a => string.Equals(a, "--preview-image", StringComparison.OrdinalIgnoreCase));
            var source = idx + 1 < args.Length ? args[idx + 1] : null;
            return await PreviewImageAsync(source);
        }

        return RunSelfTest();
    }

    private static bool TestCellGeometry()
    {
        // Exercise the native probe too (including the macOS ARM64 ABI path).
        var original = Environment.GetEnvironmentVariable("KGTEST_CELL_SIZE");
        try
        {
            Environment.SetEnvironmentVariable("KGTEST_CELL_SIZE", null);
            var detected = TerminalCapabilities.GetCellPixelSize();
            if (detected.Width <= 0 || detected.Height <= 0)
            {
                throw new InvalidOperationException("Invalid detected cell size.");
            }

            Environment.SetEnvironmentVariable("KGTEST_CELL_SIZE", "8x20");
            var canvas = new Canvas();
            using var renderer = new TerminalRenderer();
            var area = new Rect(0, 0, 40, 20);
            if (!renderer.UpdateCellPixelSize(canvas, TerminalCapabilities.GetCellPixelSize()) ||
                canvas.CellAspect != 2.5 || Canvas.FitImage(area, 1, canvas.CellAspect).H != 16 ||
                renderer.UpdateCellPixelSize(canvas, (8, 20)))
            {
                throw new InvalidOperationException("Initial cell geometry mismatch.");
            }

            // Same terminal rows/columns, different font geometry.
            Environment.SetEnvironmentVariable("KGTEST_CELL_SIZE", "10X20");
            if (!renderer.UpdateCellPixelSize(canvas, TerminalCapabilities.GetCellPixelSize()) ||
                canvas.CellAspect != 2 || Canvas.FitImage(area, 1, canvas.CellAspect).H != 20 ||
                renderer.CellPixelSize != (10, 20))
            {
                throw new InvalidOperationException("Changed cell geometry was not refreshed.");
            }

            Environment.SetEnvironmentVariable("KGTEST_CELL_SIZE", "0x20");
            if (TerminalCapabilities.GetCellPixelSize() != detected)
            {
                throw new InvalidOperationException("Invalid override did not fall back to detection.");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cell geometry self-test failed: {ex.Message}");
            return false;
        }
        finally
        {
            Environment.SetEnvironmentVariable("KGTEST_CELL_SIZE", original);
        }
    }

    private static int RunSelfTest()
    {
        if (!TestCellGeometry())
        {
            return 1;
        }

        // 1. 构造合成二维码位图并测试模块还原。
        const int modules = 25;
        const int scale = 8;
        const int border = 16;
        var qrSize = modules * scale + border * 2;
        using var qrBitmap = new SKBitmap(qrSize, qrSize);
        using (var canvas = new SKCanvas(qrBitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            DrawFinder(canvas, paint, border, border, scale);
            DrawFinder(canvas, paint, border + (modules - 7) * scale, border, scale);
            DrawFinder(canvas, paint, border, border + (modules - 7) * scale, scale);
            for (var y = 8; y < modules - 8; y++)
            {
                for (var x = 8; x < modules - 8; x++)
                {
                    if ((x + y) % 2 == 0)
                    {
                        canvas.DrawRect(border + x * scale, border + y * scale, scale, scale, paint);
                    }
                }
            }
        }

        var extracted = QrMatrixExtractor.Extract(qrBitmap);
        if (extracted == null || extracted.GetLength(0) != modules)
        {
            Console.Error.WriteLine("QR matrix extraction failed.");
            return 1;
        }

        // 2. 构造渐变封面图并验证三种图形协议编码与半块字符绘制。
        using var coverBitmap = BuildSyntheticCover(240, 240);
        using var qrImage = new TerminalImage("test:qr", qrBitmap.Copy(), crisp: true);
        using var coverImage = new TerminalImage("test:cover", coverBitmap.Copy(), crisp: false);

        var sb = new System.Text.StringBuilder();
        ImageProtocolEncoder.AppendKitty(sb, coverImage, 1, 20, 10, 160, 160);
        ImageProtocolEncoder.AppendITerm(sb, coverImage, 20, 10, 160, 160);
        ImageProtocolEncoder.AppendSixel(sb, coverImage, 120, 120);
        if (sb.Length < 512)
        {
            Console.Error.WriteLine("Image protocol encoding produced unexpectedly short output.");
            return 1;
        }

        // 3. 在离屏画布上分别渲染二维码与沉浸页。
        var tui = new Canvas { ImageMode = TerminalImageMode.Blocks };
        var theme = new Theme();
        using var cache = new ImageCache(() => { });
        tui.Reset(80, 18);
        if (tui.Qr(new Rect(2, 1, 40, 16), qrImage).IsEmpty)
        {
            Console.Error.WriteLine("QR rendering failed.");
            return 1;
        }

        tui.Reset(128, 36);

        var snapshot = new PlaybackStateSnapshot
        {
            CurrentSong = new TerminalSongItem
            {
                Name = "测试曲目 - 终端图形渲染",
                Singer = "Kugou TUI",
                Source = "SelfTest",
                DurationSeconds = 215,
                IsPlaying = true
            },
            IsPlaying = true,
            Volume = 0.8f,
            Position = TimeSpan.FromSeconds(68),
            Duration = TimeSpan.FromSeconds(215),
            LyricLines =
            [
                new TerminalLyricLineView { Offset = -1, Primary = "上一句歌词" },
                new TerminalLyricLineView
                {
                    Offset = 0,
                    IsCurrent = true,
                    Primary = "正在播放的逐字高亮歌词",
                    Secondary = "Translated lyric line",
                    Words = [("正在播放的", 1f), ("逐字", 0.6f), ("高亮歌词", 0f)]
                },
                new TerminalLyricLineView { Offset = 1, Primary = "下一句歌词" }
            ],
            SpectrumBands = [0.2f, 0.5f, 0.8f, 0.6f, 0.9f, 0.4f, 0.7f, 0.3f],
            Rms = 0.12
        };

        var visualizer = new VisualizerView();
        visualizer.RenderImmersive(tui, snapshot, cache, theme);

        // 4. 验证 Windows DPAPI 返回失败或抛异常时的会话持久化安全行为。
        if (!VerifySessionPersistenceSecurity())
        {
            return 1;
        }

        // 5. 验证探索发现卡片页渲染、二维卡片导航与本地音乐库 SQLite 导入逻辑。
        if (!VerifyExploreAndLocalMusic(tui, cache, theme))
        {
            return 1;
        }

        Console.WriteLine(tui.Snapshot());
        Console.WriteLine($"Self-test OK · Accent=#{coverImage.Accent.R:X2}{coverImage.Accent.G:X2}{coverImage.Accent.B:X2} · EncodedBytes={sb.Length} · SessionSecurity=Verified · ExploreAndLocal=Verified");
        return 0;
    }

    // 验证探索发现卡片网格渲染、二维焦点移动、未登录在线鉴权判断与本地音乐库 SQLite 导入。
    private static bool VerifyExploreAndLocalMusic(Canvas tui, ImageCache cache, Theme theme)
    {
        var exploreState = new ExploreViewState
        {
            TopCards =
            [
                new TerminalExploreCard
                {
                    Id = "topcard:1",
                    Title = "私人专属好歌",
                    Subtitle = "懂你的每日推荐",
                    Badge = "个性推荐 · 20首",
                    SectionTitle = "官方推荐卡",
                    Kind = TerminalExploreCardKind.TopCard,
                    PreviewLines = ["1. 晴天 - 周杰伦", "2. 泡沫 - G.E.M.邓紫棋"]
                },
                new TerminalExploreCard
                {
                    Id = "topcard:2",
                    Title = "经典怀旧金曲",
                    Subtitle = "跨越时光的旋律",
                    Badge = "岁月留声 · 20首",
                    SectionTitle = "官方推荐卡",
                    Kind = TerminalExploreCardKind.TopCard,
                    PreviewLines = ["1. 海阔天空 - Beyond", "2. 红日 - 李克勤"]
                }
            ],
            FeaturedRankCards =
            [
                new TerminalExploreCard
                {
                    Id = "rank:8888",
                    Title = "酷狗TOP500",
                    Subtitle = "榜单精选",
                    Badge = "每日更新",
                    SectionTitle = "榜单精选",
                    Kind = TerminalExploreCardKind.Rank,
                    PreviewLines = ["1. 热门单曲 A", "2. 热门单曲 B", "3. 热门单曲 C"]
                }
            ]
        };

        tui.Reset(128, 36);
        ExploreView.Render(tui, new Rect(0, 0, 100, 32), exploreState, focused: true, isLoading: false, cache, theme);
        if (exploreState.CardHitBoxes.Count < 3 || exploreState.TabHitBoxes.Count < 4)
        {
            Console.Error.WriteLine("ExploreView self-test failed: expected card and tab hitboxes.");
            return false;
        }

        var moved = ExploreView.MoveCardSelection(0, dx: 1, dy: 0, count: 3, columns: 2);
        if (moved != 1)
        {
            Console.Error.WriteLine("ExploreView self-test failed: horizontal card movement mismatch.");
            return false;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"kamusic-local-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var dbPath = Path.Combine(tempRoot, "kugou-player.db");
            var musicFolder = Path.Combine(tempRoot, "MyLocalTracks");
            Directory.CreateDirectory(musicFolder);
            var dummyWav = Path.Combine(musicFolder, "TestSong.wav");
            var wavBytes = new byte[128];
            "RIFF"u8.CopyTo(wavBytes.AsSpan(0));
            BitConverter.TryWriteBytes(wavBytes.AsSpan(4), 120);
            "WAVEfmt "u8.CopyTo(wavBytes.AsSpan(8));
            BitConverter.TryWriteBytes(wavBytes.AsSpan(16), 16);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(20), (short)1);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(22), (short)1);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(24), 44100);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(28), 88200);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(32), (short)2);
            BitConverter.TryWriteBytes(wavBytes.AsSpan(34), (short)16);
            "data"u8.CopyTo(wavBytes.AsSpan(36));
            BitConverter.TryWriteBytes(wavBytes.AsSpan(40), 84);
            File.WriteAllBytes(dummyWav, wavBytes);

            var localService = new TerminalLocalMusicService(dbPath);
            var (importedPlaylist, count) = localService.ImportPathAsync(musicFolder).GetAwaiter().GetResult();
            var tracks = localService.GetPlaylistTracksAsync(importedPlaylist.LocalPlaylistId).GetAwaiter().GetResult();
            if (count != 1 || tracks.Count != 1 || !tracks[0].IsLocalFile || tracks[0].RequiresOnlineAuth)
            {
                Console.Error.WriteLine("LocalMusic self-test failed: imported folder track validation failed.");
                return false;
            }

            var onlineSong = new TerminalSongItem { Name = "Online", Hash = "ABCD1234" };
            if (!onlineSong.RequiresOnlineAuth || onlineSong.IsLocalFile)
            {
                Console.Error.WriteLine("LocalMusic self-test failed: online song auth requirement mismatch.");
                return false;
            }

            var coverUri = TerminalLocalMusicService.BuildEmbeddedCoverSource(dummyWav);
            if (!TerminalLocalMusicService.TryParseEmbeddedCoverSource(coverUri, out var parsedPath) ||
                !string.Equals(Path.GetFullPath(dummyWav), parsedPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("LocalMusic self-test failed: embedded cover URI round-trip mismatch.");
                return false;
            }

            var singerDetail = new TerminalSingerDetail
            {
                AuthorId = 12345,
                Name = "测试歌手",
                Tab = TerminalSingerTab.HotSongs
            };
            singerDetail.HotSongs.Add(new TerminalSongItem
            {
                Name = "热门歌曲1",
                Singer = "测试歌手",
                Hash = "HASH1",
                IsFavorite = true
            });
            singerDetail.Albums.Add(new TerminalSingerAlbumItem
            {
                AlbumId = 888,
                Name = "测试专辑",
                PublishDate = "2024-01-01"
            });

            tui.Reset(100, 30);
            WorkspaceViews.RenderSingerPage(tui, new Rect(0, 0, 100, 30), singerDetail, 0, true, false, cache, theme);
            tui.Reset(100, 30);
            singerDetail.Tab = TerminalSingerTab.Albums;
            WorkspaceViews.RenderSingerPage(tui, new Rect(0, 0, 100, 30), singerDetail, 0, true, false, cache, theme);

            tui.Reset(100, 30);
            var testPlaylists = new List<TerminalPlaylistItem>
            {
                new() { Name = "我喜欢", ListId = "1", Count = 12, Kind = TerminalPlaylistKind.Online },
                new() { Name = "轻音乐", ListId = "2", Count = 5, Kind = TerminalPlaylistKind.Online }
            };
            WorkspaceViews.RenderPlaylistPickerModal(tui, "测试歌曲", testPlaylists, 0, theme);

            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    // 验证 Windows DPAPI 失败/异常时不会将明文 Token 落盘、保留已有文件与内存会话，且警告日志不含 Token。
    private static bool VerifySessionPersistenceSecurity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"kgtest-session-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            const string existingToken = "EXISTING_TOKEN_111111";
            const string secretTokenFalse = "SECRET_TOKEN_DPAPI_FALSE_222222";
            const string secretVipFalse = "SECRET_VIP_DPAPI_FALSE_333333";
            const string secretT1False = "SECRET_T1_DPAPI_FALSE_444444";
            const string secretTokenThrow = "SECRET_TOKEN_DPAPI_THROW_555555";
            const string secretVipThrow = "SECRET_VIP_DPAPI_THROW_666666";
            const string secretT1Throw = "SECRET_T1_DPAPI_THROW_777777";

            var sessionPath = Path.Combine(tempDir, "KgTestSession.json");
            var legacyPath = Path.Combine(tempDir, "session.json");
            var warnings = new List<string>();

            static bool XorTransform(byte[] input, bool protect, out byte[] output)
            {
                output = new byte[input.Length];
                for (var i = 0; i < input.Length; i++)
                {
                    output[i] = (byte)(input[i] ^ 0x5A);
                }

                return true;
            }

            // (a) 先写入一份合法的加密会话文件，作为已有文件基线。
            var okPersistence = new TerminalSessionPersistence(
                sessionPath,
                legacyPath,
                isWindows: static () => true,
                dpapiTransform: XorTransform,
                warningLogger: warnings.Add);

            var manager = new KgSessionManager(new CookieContainer(), okPersistence);
            manager.UpdateAuth("10001", existingToken, "1", "EXISTING_VIP", "EXISTING_T1");

            if (!File.Exists(sessionPath))
            {
                Console.Error.WriteLine("Session self-test failed: baseline encrypted session file was not created.");
                return false;
            }

            var baselineEncryptedContent = File.ReadAllText(sessionPath);
            if (!baselineEncryptedContent.StartsWith(TerminalSessionPersistence.ProtectedSessionPrefix, StringComparison.Ordinal) ||
                baselineEncryptedContent.Contains(existingToken, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Session self-test failed: baseline session file is not encrypted.");
                return false;
            }

            // (b) 测试 DPAPI 返回 false：中止保存、保留内存会话与磁盘已有文件、记录不含 Token 的警告。
            warnings.Clear();
            var failPersistence = new TerminalSessionPersistence(
                sessionPath,
                legacyPath,
                isWindows: static () => true,
                dpapiTransform: (byte[] _, bool _, out byte[] output) =>
                {
                    output = [];
                    return false;
                },
                warningLogger: warnings.Add);

            var failManager = new KgSessionManager(new CookieContainer(), failPersistence);
            warnings.Clear();
            failManager.UpdateAuth("10002", secretTokenFalse, "6", secretVipFalse, secretT1False);

            var afterFalseDiskContent = File.ReadAllText(sessionPath);
            if (afterFalseDiskContent != baselineEncryptedContent ||
                File.Exists(sessionPath + ".tmp") ||
                failManager.Session.Token != secretTokenFalse ||
                warnings.Count == 0 ||
                warnings.Any(w =>
                    w.Contains(secretTokenFalse, StringComparison.Ordinal) ||
                    w.Contains(secretVipFalse, StringComparison.Ordinal) ||
                    w.Contains(secretT1False, StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("Session self-test failed: DPAPI false branch did not preserve file/memory or leaked token.");
                return false;
            }

            // (c) 测试 DPAPI 抛异常（即使异常消息本身带有 Token 也不得泄露到警告日志）：中止保存、保留内存会话与磁盘已有文件。
            warnings.Clear();
            var throwPersistence = new TerminalSessionPersistence(
                sessionPath,
                legacyPath,
                isWindows: static () => true,
                dpapiTransform: (byte[] _, bool _, out byte[] _) =>
                    throw new CryptographicException($"Simulated DPAPI failure with {secretTokenThrow}"),
                warningLogger: warnings.Add);

            var throwManager = new KgSessionManager(new CookieContainer(), throwPersistence);
            warnings.Clear();
            throwManager.UpdateAuth("10003", secretTokenThrow, "6", secretVipThrow, secretT1Throw);

            var afterThrowDiskContent = File.ReadAllText(sessionPath);
            if (afterThrowDiskContent != baselineEncryptedContent ||
                File.Exists(sessionPath + ".tmp") ||
                throwManager.Session.Token != secretTokenThrow ||
                warnings.Count == 0 ||
                warnings.Any(w =>
                    w.Contains(secretTokenThrow, StringComparison.Ordinal) ||
                    w.Contains(secretVipThrow, StringComparison.Ordinal) ||
                    w.Contains(secretT1Throw, StringComparison.Ordinal)))
            {
                Console.Error.WriteLine("Session self-test failed: DPAPI exception branch did not preserve file/memory or leaked token.");
                return false;
            }

            // (d) 验证磁盘上的已有加密会话仍可正常解密读取。
            var reloaded = okPersistence.Load();
            if (reloaded == null || reloaded.Token != existingToken)
            {
                Console.Error.WriteLine("Session self-test failed: existing encrypted session could not be reloaded.");
                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task<int> PreviewImageAsync(string? source)
    {
        SKBitmap? bitmap = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            bitmap = BuildSyntheticCover(320, 320);
        }
        else if (File.Exists(source))
        {
            bitmap = SKBitmap.Decode(source);
        }
        else if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(source);
            bitmap = SKBitmap.Decode(bytes);
        }

        if (bitmap == null)
        {
            Console.Error.WriteLine("无法加载图片。");
            return 1;
        }

        using (bitmap)
        using (var image = new TerminalImage("preview", bitmap.Copy(), crisp: false))
        {
            var mode = TerminalCapabilities.DetectImageMode();
            var cellPixelSize = TerminalCapabilities.GetCellPixelSize();
            var canvas = new Canvas
            {
                ImageMode = mode,
                CellAspect = cellPixelSize.Height / (double)cellPixelSize.Width
            };
            canvas.Reset(80, 24);
            canvas.Image("preview", new Rect(2, 1, 44, 20), image);
            using var renderer = new TerminalRenderer
            {
                TrueColor = TerminalCapabilities.DetectTrueColor(),
                CellPixelSize = cellPixelSize
            };
            renderer.Present(canvas);
            Console.WriteLine();
        }

        return 0;
    }

    private static void DrawFinder(SKCanvas canvas, SKPaint paint, int x, int y, int s)
    {
        canvas.DrawRect(x, y, 7 * s, 7 * s, paint);
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x + s, y + s, 5 * s, 5 * s, white);
        canvas.DrawRect(x + 2 * s, y + 2 * s, 3 * s, 3 * s, paint);
    }

    private static SKBitmap BuildSyntheticCover(int width, int height)
    {
        var bmp = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(40 + x * 190 / width);
                var g = (byte)(60 + y * 150 / height);
                var b = (byte)(180 - x * 100 / width);
                bmp.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        return bmp;
    }
}
