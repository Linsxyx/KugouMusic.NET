# 酷狗安全验证（SSA）

桌面端发送登录短信或提交手机验证码登录时，SDK 会保留酷狗响应头中的 `ssa-code`，并通过模型的 `SsaCode` / `RequiresVerification` 暴露验证事件。业务错误码 `20028` 缺少事件标识时显示错误，不发送空事件请求。

## 桌面交互

1. 获取验证信息，打开独立的 Avalonia `NativeWebView` 窗口。
2. `v_type=23` 展示腾讯验证码；`v_type=32` 接收安全验证短信验证码。
3. 页面使用参考项目的 WASM 组件生成 `sid/edt`，C# 使用原 SDK 会话提交验证结果。
4. 只有服务端明确返回成功时，才重试一次原操作；重复挑战不会无限弹窗。
5. 关闭窗口、点击取消、编辑登录信息、切换登录方式或关闭登录界面均取消当前验证。验证等待上限为五分钟。

网页资源内嵌于程序集，通过临时 `127.0.0.1` HTTP 端口加载。页面路径包含随机会话标识，提交要求同源 JSON POST；服务仅提供指定的资源和验证操作，结束时关闭。账号 Token 不传给 WebView。

运行库：Windows 使用 WebView2 Runtime；macOS 使用系统 WKWebView；Linux 需要 Avalonia WebView 支持的 WPE/WebKitGTK 依赖。不存在可用 WebView 时不会自动安装运行库。

## SDK 和 Web API

- `VerificationClient.GetInfoAsync(eventId)` 获取验证方式。
- `VerificationClient.SubmitAsync(eventId, verificationType, verifyCode, sid, edt)` 提交用户完成的验证。
- 字符串参数应传原始值，不要提前 URL 编码。腾讯票据格式为 `KGCodeTX|` 加上含 `ticket`、`randstr`、`txappid` 的 JSON。
- 通过 `KuGouComposition.Root.Verification` 或依赖注入取得客户端。
- Web API：`GET /captcha/verify/info?eventId=...` 与 `POST /captcha/verify`，后者使用 JSON 属性 `eventId`、`verificationType`、`verifyCode`、`sid`、`edt`。调用时沿用触发挑战时的会话。
- SDK 对成功及业务失败响应均保留验证事件；带验证头但没有有效 JSON 时抛出 `KgVerificationRequiredException`，其中保留事件标识及 HTTP 状态。
- SDK 本身不弹窗、不自动重放任意业务请求。桌面自动恢复目前接入发送登录短信和手机验证码登录。

## 验证

自动测试：`dotnet test src/Tests/KuGou.Net.Tests/KuGou.Net.Tests.csproj`。

真实联调需要酷狗返回有效挑战：检查窗口能加载验证码，用户完成后酷狗确认成功，原操作只重试一次；同时检查取消、验证失败、缺少事件标识和重复挑战。模拟后端测试只能证明本地交互及协议结构，不能证明酷狗一定接受票据。macOS/Linux 的原生 WebView 需要在对应系统验证。

`Services/SecurityVerification/verifycode.js` 与 `verifycode_bg_ios.wasm` 来自 `external/KuGouMusicApi/public/verify-pkg`；保留参考项目许可证于 `LICENSE.reference.txt`。
