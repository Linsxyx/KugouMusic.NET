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
    `# ${MAIN_PKG_NAME} (KA Music CLI)\n\n最好用、最轻量的酷狗音乐概念版终端 TUI 播放器（NativeAOT 原生编译，跨 Windows / macOS / Linux 全平台）。\n\n## 安装与启动\n\n\`\`\`bash\n# 全局安装\nnpm install -g ${MAIN_PKG_NAME}\n\n# 启动播放器\nkamusic\n\n# 检查并更新到最新版本\nkamusic update\n\`\`\`\n`
  );

  if (await isAlreadyPublished(MAIN_PKG_NAME, version)) {
    console.log(`✓ Skipping ${MAIN_PKG_NAME}@${version} (already published)`);
  } else {
    console.log(`=== Publishing ${MAIN_PKG_NAME}@${version} ===`);
    execFileSync("npm", ["publish", "--access", "public"], { cwd: mainDir, stdio: "inherit" });
  }
}

main();
