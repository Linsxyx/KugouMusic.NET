#!/usr/bin/env node

// 在 GitHub Actions 中将 6 平台 NativeAOT 构建产物组装为 6 个 @linsxyx/kamusic-<os>-<cpu> 子包与 @linsxyx/kamusic 主包，并自动发布到 npm。

import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

const NPM_SCOPE = "@linsxyx";
const MAIN_PKG_NAME = `${NPM_SCOPE}/kamusic`;

const TARGETS = [
  {
    dir: "kamusic-win32-x64",
    pkg: `${NPM_SCOPE}/kamusic-win32-x64`,
    rid: "win-x64",
    os: "win32",
    cpu: "x64",
    archive: "KaMusic.Cli-win-x64.zip",
    type: "zip",
  },
  {
    dir: "kamusic-win32-arm64",
    pkg: `${NPM_SCOPE}/kamusic-win32-arm64`,
    rid: "win-arm64",
    os: "win32",
    cpu: "arm64",
    archive: "KaMusic.Cli-win-arm64.zip",
    type: "zip",
  },
  {
    dir: "kamusic-linux-x64",
    pkg: `${NPM_SCOPE}/kamusic-linux-x64`,
    rid: "linux-x64",
    os: "linux",
    cpu: "x64",
    archive: "KaMusic.Cli-linux-x64.tar.gz",
    type: "tar.gz",
  },
  {
    dir: "kamusic-linux-arm64",
    pkg: `${NPM_SCOPE}/kamusic-linux-arm64`,
    rid: "linux-arm64",
    os: "linux",
    cpu: "arm64",
    archive: "KaMusic.Cli-linux-arm64.tar.gz",
    type: "tar.gz",
  },
  {
    dir: "kamusic-darwin-arm64",
    pkg: `${NPM_SCOPE}/kamusic-darwin-arm64`,
    rid: "osx-arm64",
    os: "darwin",
    cpu: "arm64",
    archive: "KaMusic.Cli-osx-arm64.tar.gz",
    type: "tar.gz",
  },
  {
    dir: "kamusic-darwin-x64",
    pkg: `${NPM_SCOPE}/kamusic-darwin-x64`,
    rid: "osx-x64",
    os: "darwin",
    cpu: "x64",
    archive: "KaMusic.Cli-osx-x64.tar.gz",
    type: "tar.gz",
  },
];

const LAUNCHER_JS = `#!/usr/bin/env node

// kamusic CLI 启动入口：支持 \`kamusic update\` / \`kamusic -v\`，并根据当前 OS 与 CPU 架构定位对应平台子包中的 NativeAOT 可执行文件（支持 TUI 内触发退出码 42 自动更新并重启）。

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const https = require("node:https");
const { spawn, spawnSync } = require("node:child_process");

const MAIN_PKG_NAME = "@linsxyx/kamusic";
const UPDATE_EXIT_CODE = 42;

const REGISTRY_URLS = [
  "https://registry.npmjs.org/@linsxyx%2fkamusic/latest",
  "https://registry.npmmirror.com/@linsxyx/kamusic/latest",
];

const PLATFORM_PACKAGES = {
  "win32-x64": {
    pkg: "@linsxyx/kamusic-win32-x64",
    dir: "kamusic-win32-x64",
    bin: "KaMusic.Cli.exe",
  },
  "win32-arm64": {
    pkg: "@linsxyx/kamusic-win32-arm64",
    dir: "kamusic-win32-arm64",
    bin: "KaMusic.Cli.exe",
  },
  "linux-x64": {
    pkg: "@linsxyx/kamusic-linux-x64",
    dir: "kamusic-linux-x64",
    bin: "KaMusic.Cli",
  },
  "linux-arm64": {
    pkg: "@linsxyx/kamusic-linux-arm64",
    dir: "kamusic-linux-arm64",
    bin: "KaMusic.Cli",
  },
  "darwin-arm64": {
    pkg: "@linsxyx/kamusic-darwin-arm64",
    dir: "kamusic-darwin-arm64",
    bin: "KaMusic.Cli",
  },
  "darwin-x64": {
    pkg: "@linsxyx/kamusic-darwin-x64",
    dir: "kamusic-darwin-x64",
    bin: "KaMusic.Cli",
  },
};

function getCurrentVersion() {
  try {
    const pkgPath = path.resolve(__dirname, "..", "package.json");
    const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf8"));
    return pkg.version || "0.1.0";
  } catch {
    return "0.1.0";
  }
}

function compareSemVer(a, b) {
  const pa = String(a).replace(/^v/i, "").split("-")[0].split(".");
  const pb = String(b).replace(/^v/i, "").split("-")[0].split(".");
  const len = Math.max(pa.length, pb.length);
  for (let i = 0; i < len; i++) {
    const va = Number.parseInt(pa[i] || "0", 10) || 0;
    const vb = Number.parseInt(pb[i] || "0", 10) || 0;
    if (va !== vb) return va > vb ? 1 : -1;
  }
  return 0;
}

function fetchJson(url, timeoutMs = 5000) {
  return new Promise((resolve, reject) => {
    const req = https.get(
      url,
      { headers: { "User-Agent": "kamusic-cli-updater/1.0" }, timeout: timeoutMs },
      (res) => {
        if (res.statusCode && res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          fetchJson(res.headers.location, timeoutMs).then(resolve, reject);
          return;
        }
        if (res.statusCode !== 200) {
          reject(new Error(\`HTTP \${res.statusCode}\`));
          return;
        }
        let body = "";
        res.setEncoding("utf8");
        res.on("data", (chunk) => (body += chunk));
        res.on("end", () => {
          try {
            resolve(JSON.parse(body));
          } catch (e) {
            reject(e);
          }
        });
      }
    );
    req.on("error", reject);
    req.on("timeout", () => {
      req.destroy(new Error("timeout"));
    });
  });
}

async function fetchLatestVersion() {
  for (const url of REGISTRY_URLS) {
    try {
      const data = await fetchJson(url);
      if (data && typeof data.version === "string" && data.version.trim()) {
        return data.version.trim();
      }
    } catch {
      // try next mirror
    }
  }
  return null;
}

function installGlobalPackage(targetSpec) {
  const npmCmd = process.platform === "win32" ? "npm.cmd" : "npm";
  const baseArgs = ["install", "-g", targetSpec, "--prefer-online"];

  let res = spawnSync(npmCmd, baseArgs, { stdio: "inherit", shell: process.platform === "win32" });
  if (res.status === 0) {
    return true;
  }

  console.log("[kamusic] 检测到默认 npm 缓存可能存在权限冲突，正在使用独立临时缓存重试...");
  const tempCache = path.join(os.tmpdir(), "kamusic-npm-cache");
  res = spawnSync(npmCmd, [...baseArgs, "--cache", tempCache], {
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  return res.status === 0;
}

async function handleUpdateCommand({ force = false, restartAfter = false } = {}) {
  const current = getCurrentVersion();
  console.log(\`[kamusic] 当前版本: v\${current}，正在检查 npm (\${MAIN_PKG_NAME}) 最新版本...\`);
  const latest = await fetchLatestVersion();
  if (!latest) {
    console.error("[kamusic] 无法连接 npm 源获取最新版本，请检查网络连接。");
    process.exit(1);
  }

  if (!force && compareSemVer(latest, current) <= 0) {
    console.log(\`[kamusic] 当前已是最新版本 (v\${current})。如需强制重装可执行: kamusic update --force\`);
    return false;
  }

  console.log(\`[kamusic] 发现新版本 v\${latest}，开始更新 \${MAIN_PKG_NAME}@\${latest} ...\`);
  const ok = installGlobalPackage(\`\${MAIN_PKG_NAME}@\${latest}\`);
  if (!ok) {
    console.error(\`[kamusic] 更新失败，可手动执行: npm i -g \${MAIN_PKG_NAME}@latest\`);
    process.exit(1);
  }

  console.log(\`[kamusic] ✓ 已成功更新至 v\${latest}！\`);
  if (restartAfter) {
    console.log("[kamusic] 正在重新启动播放器...");
    const res = spawnSync("kamusic", [], { stdio: "inherit", shell: process.platform === "win32" });
    process.exit(res.status ?? 0);
  }
  return true;
}

function resolveBinaryPath() {
  const key = \`\${process.platform}-\${process.arch}\`;
  const target = PLATFORM_PACKAGES[key];
  if (!target) {
    console.error(
      \`[kamusic] 当前平台暂不支持: \${key}（支持平台: \${Object.keys(PLATFORM_PACKAGES).join(", ")}）\`
    );
    process.exit(1);
  }

  const candidates = [];

  try {
    const pkgJsonPath = require.resolve(\`\${target.pkg}/package.json\`);
    candidates.push(path.join(path.dirname(pkgJsonPath), "vendor", target.bin));
  } catch {
    // fallback to workspace sibling directory
  }

  candidates.push(
    path.resolve(__dirname, "..", "..", target.dir, "vendor", target.bin)
  );

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  console.error(
    \`[kamusic] 未找到当前平台 (\${key}) 的原生可执行文件，请确认可选依赖包 "\${target.pkg}" 已安装（可执行 kamusic update --force 修复）。\`
  );
  process.exit(1);
}

async function main() {
  const args = process.argv.slice(2);
  const first = args[0];

  if (first === "-v" || first === "--version" || first === "version") {
    console.log(\`\${MAIN_PKG_NAME} v\${getCurrentVersion()} (\${process.platform}-\${process.arch})\`);
    return;
  }

  if (first === "update" || first === "upgrade" || first === "-u" || first === "--update") {
    const force = args.includes("--force") || args.includes("-f");
    await handleUpdateCommand({ force, restartAfter: false });
    return;
  }

  if (first === "check" || first === "--check-update") {
    const current = getCurrentVersion();
    const latest = await fetchLatestVersion();
    if (!latest) {
      console.error("[kamusic] 检查更新失败：无法连接 npm 源。");
      process.exit(1);
    }
    if (compareSemVer(latest, current) > 0) {
      console.log(\`[kamusic] 发现新版本: v\${current} -> v\${latest}（运行 \\\`kamusic update\\\` 即可更新）\`);
    } else {
      console.log(\`[kamusic] 当前已是最新版本: v\${current}\`);
    }
    return;
  }

  const binPath = resolveBinaryPath();
  if (process.platform !== "win32") {
    try {
      fs.chmodSync(binPath, 0o755);
    } catch {
      // ignore chmod errors on read-only mounts
    }
  }

  const currentVersion = getCurrentVersion();
  const child = spawn(binPath, args, {
    stdio: "inherit",
    windowsHide: false,
    env: {
      ...process.env,
      KAMUSIC_NPM_WRAPPER: "1",
      KAMUSIC_VERSION: currentVersion,
    },
  });

  for (const sig of ["SIGINT", "SIGTERM", "SIGHUP"]) {
    process.on(sig, () => {
      if (!child.killed) {
        try {
          child.kill(sig);
        } catch {
          // ignore
        }
      }
    });
  }

  child.on("error", (err) => {
    console.error(\`[kamusic] 启动失败: \${err.message}\`);
    process.exit(1);
  });

  child.on("exit", async (code, signal) => {
    if (signal) {
      process.kill(process.pid, signal);
      return;
    }
    if (code === UPDATE_EXIT_CODE) {
      await handleUpdateCommand({ force: false, restartAfter: true });
      return;
    }
    process.exit(code ?? 0);
  });
}

main();
`;

async function isAlreadyPublished(pkgName, version) {
  try {
    const encoded = pkgName.replace("/", "%2f");
    const res = await fetch(`https://registry.npmjs.org/${encoded}/${version}`);
    return res.status === 200;
  } catch {
    return false;
  }
}

// 生成发布至 npm 的主包完整自述文档（包含安装指南、全平台架构表、终端图片与快捷键说明）。
function buildMainReadme(pkgName) {
  return `# ${pkgName} (KA Music CLI)

[![npm version](https://img.shields.io/npm/v/${pkgName}.svg)](https://www.npmjs.com/package/${pkgName})
[![GitHub](https://img.shields.io/badge/GitHub-KugouMusic.NET-blue.svg)](https://github.com/Linsxyx/KugouMusic.NET)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/Linsxyx/KugouMusic.NET/blob/master/LICENSE)

极速、轻量、高颜值的酷狗音乐概念版终端 TUI 播放器。基于 NativeAOT 原生编译分发，跨 Windows / macOS / Linux 全平台支持，**无需安装任何 .NET SDK 或运行库**。

## 安装与启动

### 全局安装（推荐）

\`\`\`bash
# 全局安装
npm install -g ${pkgName}

# 启动播放器
kamusic

# 检查并一键更新到最新版本
kamusic update
\`\`\`

### 免安装直接运行

\`\`\`bash
npx ${pkgName}
\`\`\`

## 平台与架构支持

本包通过 npm \`optionalDependencies\` 按系统与 CPU 架构自动下载对应的 NativeAOT 原生二进制包，开箱即用：

| 平台 | 架构 | 子包名称 |
| --- | --- | --- |
| Windows | x64 | \`${pkgName}-win32-x64\` |
| Windows | ARM64 | \`${pkgName}-win32-arm64\` |
| Linux | x64 | \`${pkgName}-linux-x64\` |
| Linux | ARM64 | \`${pkgName}-linux-arm64\` |
| macOS | Apple Silicon (arm64) | \`${pkgName}-darwin-arm64\` |
| macOS | Intel (x64) | \`${pkgName}-darwin-x64\` |

> Linux 说明：音频输出依赖系统 ALSA/PulseAudio/PipeWire 库环境（如 \`libasound2\` 或 \`pulseaudio\`），大多数桌面发行版已默认预装。

## 核心特性

- **多协议终端高清图片渲染**：
  - 自动检测终端能力，优先采用 **Kitty Graphics Protocol**、**iTerm2 (\`OSC 1337\`)** 或 **Sixel** 输出高清位图。
  - 在普通终端自动降级为 UTF-8 半块字符（\`▀\`）双像素采样；登录二维码采用专用模块矩阵绘制，确保手机轻松扫码。
  - 在终端内直接渲染**登录二维码、专辑封面、歌单封面与沉浸大图**，并自动提取主题强调色。
- **全功能音频体验**：
  - 支持每日推荐、私人 FM、发现歌单、排行榜、歌曲与歌单搜索、我的收藏歌单。
  - **KRC 逐字高亮歌词**：支持平滑逐字推进动画。
  - **沉浸式播放页（按 \`i\` 开启）**：大封面 + 逐字歌词 + 实时音频频谱可视化。
  - **专业音效**：10 段均衡器（EQ 预设/自定义）与无缝音频智能过渡。
- **键盘与鼠标双交互**：
  - 支持完整的键盘快捷键操控（类 Vim 键位 \`j\`/\`k\`、翻页、快进快退）。
  - 支持终端鼠标点击切换页面、双击播放歌曲、滚轮翻页等交互。
- **安全与独立会话**：
  - 与桌面客户端分离存储（\`ApplicationData/kugou/KgTestSession.json\`），互不干扰。
  - Windows 系统下通过 DPAPI 加密保护登录凭据与 Token。

## 页面导航

| 按键 | 页面 | 说明 |
| :---: | --- | --- |
| \`1\` | 每日推荐 | 每日个性化推荐歌曲 |
| \`2\` | 私人 FM | 私人电台流，支持标记不喜欢（\`d\`） |
| \`3\` | 发现歌单 | 精选热门与主题歌单 |
| \`4\` | 排行榜 | 官方各分类热门榜单 |
| \`5\` | 搜索 | 歌曲与歌单搜索（同一页面按 \`v\` 切换结果类型） |
| \`6\` | 我的歌单 | 登录账号下的收藏与自建歌单 |
| \`7\` | 播放队列 | 查看当前待播歌曲列表（\`x\` 移除歌曲） |
| \`8\` | 设置 | 音频输出、图片渲染协议、均衡器等选项 |
| \`9\` | 账号与登录 | 支持扫码登录与手机短信验证码登录 |

## 常用快捷键

| 快捷键 | 功能 |
| --- | --- |
| \`Space\` | 播放 / 暂停 |
| \`n\` / \`b\` | 下一首 / 上一首 |
| \`[\` / \`]\` | 快退 / 快进 10 秒 |
| \`+\` / \`-\` | 调节音量（设置页内微调参数或 EQ） |
| \`i\` | 进入 / 退出沉浸式大封面歌词频谱页 |
| \`s\` | 切换循环 / 随机播放模式 |
| \`t\` | 开启 / 关闭音频智能无缝过渡 |
| \`l\` | 切换歌词显示模式 |
| \`p\` | 循环切换终端图片渲染协议（Kitty / ITerm / Sixel / Blocks / Off） |
| \`a\` | 将选中歌曲加入“下一首播放” |
| \`v\` | 在搜索页面切换“歌曲”与“歌单”结果 |
| \`/\` | 唤出全局快速搜索弹窗 |
| \`Tab\` / \`←\` / \`→\` | 在左侧导航栏与右侧内容区切换焦点 |
| \`↑\` / \`↓\` 或 \`j\` / \`k\` | 列表上下移动选择 |
| \`Enter\` | 播放选中歌曲 / 打开歌单 / 确认操作 |
| \`Backspace\` / \`Esc\` | 从歌单详情返回 / 关闭弹窗 |
| \`F5\` / \`F6\` | 扫码登录 / 手机验证码登录 |
| \`r\` | 刷新当前页面数据 |
| \`?\` | 打开快捷键帮助弹窗 |
| \`q\` | 返回上一层或退出程序 |

## 诊断命令

\`\`\`bash
# 二维码、图片协议与离屏 UI 自动检测
kamusic --self-test

# 在当前终端预览任意本地或网络图片
kamusic --preview-image <path-or-url>
\`\`\`

## 相关链接

- GitHub 源码仓库：[Linsxyx/KugouMusic.NET](https://github.com/Linsxyx/KugouMusic.NET)
- 问题反馈与建议：[GitHub Issues](https://github.com/Linsxyx/KugouMusic.NET/issues)
- 桌面客户端（Avalonia GUI）：[Releases 下载页面](https://github.com/Linsxyx/KugouMusic.NET/releases)
`;
}

async function main() {
  const artifactsDir = path.resolve(process.argv[2] || "./all_cli_artifacts");
  const version = (process.argv[3] || "0.1.0").replace(/^cli-v/, "").replace(/^v/, "");
  const workDir = path.resolve("./npm_build_packages");

  fs.rmSync(workDir, { recursive: true, force: true });
  fs.mkdirSync(workDir, { recursive: true });

  // 1. 收集所有压缩包
  const archivesDir = path.join(workDir, "_archives");
  fs.mkdirSync(archivesDir, { recursive: true });
  for (const sub of fs.readdirSync(artifactsDir)) {
    const subPath = path.join(artifactsDir, sub);
    if (fs.statSync(subPath).isDirectory()) {
      for (const file of fs.readdirSync(subPath)) {
        fs.copyFileSync(path.join(subPath, file), path.join(archivesDir, file));
      }
    }
  }

  // 2. 构建并发布 6 个平台子包
  const optionalDependencies = {};
  for (const target of TARGETS) {
    optionalDependencies[target.pkg] = version;
    const archivePath = path.join(archivesDir, target.archive);
    if (!fs.existsSync(archivePath)) {
      throw new Error(`Missing archive: ${archivePath}`);
    }

    const pkgDir = path.join(workDir, target.dir);
    const vendorDir = path.join(pkgDir, "vendor");
    const tmpExtract = path.join(pkgDir, ".tmp");
    fs.mkdirSync(tmpExtract, { recursive: true });

    if (target.type === "tar.gz") {
      execFileSync("tar", ["-xzf", archivePath, "-C", tmpExtract], { stdio: "inherit" });
    } else {
      execFileSync("unzip", ["-q", archivePath, "-d", tmpExtract], { stdio: "inherit" });
    }

    const nested = path.join(tmpExtract, `KaMusic.Cli-${target.rid}`);
    fs.renameSync(fs.existsSync(nested) ? nested : tmpExtract, vendorDir);
    fs.rmSync(tmpExtract, { recursive: true, force: true });

    const unixBin = path.join(vendorDir, "KaMusic.Cli");
    if (fs.existsSync(unixBin)) {
      fs.chmodSync(unixBin, 0o755);
    }

    const pkgJson = {
      name: target.pkg,
      version,
      description: `KA Music CLI (${target.os}-${target.cpu}) NativeAOT binary package`,
      license: "MIT",
      repository: {
        type: "git",
        url: "git+https://github.com/Linsxyx/KugouMusic.NET.git",
      },
      os: [target.os],
      cpu: [target.cpu],
      files: ["vendor/"],
      preferUnplugged: true,
      publishConfig: {
        access: "public",
      },
    };

    fs.writeFileSync(path.join(pkgDir, "package.json"), JSON.stringify(pkgJson, null, 2) + "\n");
    fs.writeFileSync(
      path.join(pkgDir, "README.md"),
      `# ${target.pkg}\n\n\`${MAIN_PKG_NAME}\` 的 \`${target.os}-${target.cpu}\` 平台原生二进制子包。请直接安装主包：\n\n\`\`\`bash\nnpm install -g ${MAIN_PKG_NAME}\nkamusic\n\`\`\`\n`
    );

    if (await isAlreadyPublished(target.pkg, version)) {
      console.log(`✓ Skipping ${target.pkg}@${version} (already published)`);
    } else {
      console.log(`=== Publishing ${target.pkg}@${version} ===`);
      execFileSync("npm", ["publish", "--access", "public"], { cwd: pkgDir, stdio: "inherit" });
    }
  }

  // 3. 构建并发布主包 @linsxyx/kamusic
  const mainDir = path.join(workDir, "kamusic");
  const binDir = path.join(mainDir, "bin");
  fs.mkdirSync(binDir, { recursive: true });

  const launcherPath = path.join(binDir, "kamusic.js");
  fs.writeFileSync(launcherPath, LAUNCHER_JS);
  fs.chmodSync(launcherPath, 0o755);

  const mainPkgJson = {
    name: MAIN_PKG_NAME,
    version,
    description:
      "KA Music 终端 TUI 播放器（NativeAOT 跨平台命令行版，支持终端专辑封面、KRC 逐字歌词、频谱与酷狗账号登录）",
    license: "MIT",
    repository: {
      type: "git",
      url: "git+https://github.com/Linsxyx/KugouMusic.NET.git",
    },
    homepage: "https://github.com/Linsxyx/KugouMusic.NET",
    bugs: {
      url: "https://github.com/Linsxyx/KugouMusic.NET/issues",
    },
    keywords: ["kamusic", "kugou", "music", "player", "tui", "cli", "terminal"],
    bin: {
      kamusic: "bin/kamusic.js",
    },
    files: ["bin/"],
    engines: {
      node: ">=16.0.0",
    },
    publishConfig: {
      access: "public",
    },
    optionalDependencies,
  };

  fs.writeFileSync(path.join(mainDir, "package.json"), JSON.stringify(mainPkgJson, null, 2) + "\n");
  fs.writeFileSync(
    path.join(mainDir, "README.md"),
    buildMainReadme(MAIN_PKG_NAME)
  );

  if (await isAlreadyPublished(MAIN_PKG_NAME, version)) {
    console.log(`✓ Skipping ${MAIN_PKG_NAME}@${version} (already published)`);
  } else {
    console.log(`=== Publishing ${MAIN_PKG_NAME}@${version} ===`);
    execFileSync("npm", ["publish", "--access", "public"], { cwd: mainDir, stdio: "inherit" });
  }
}

main();
