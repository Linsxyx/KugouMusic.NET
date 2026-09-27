/* User-completed verification; sid/edt are produced by the reference WASM runtime. */
(async () => {
  const status = document.getElementById('status');
  const verify = document.getElementById('verify');
  const cancel = document.getElementById('cancel');
  let finished = false;
  let busy = false;
  let eData;
  const post = async (path, body) => {
    const response = await fetch(path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    if (!response.ok) throw new Error('验证会话已失效，请返回播放器重试。');
    return response.json();
  };
  cancel.onclick = async () => {
    if (finished) return;
    try { await post('cancel', {}); } catch { /* The desktop may already have closed the session. */ }
    finished = true;
    verify.disabled = cancel.disabled = true;
    status.textContent = '已取消，请返回播放器。';
  };
  try {
    const response = await fetch('config');
    if (!response.ok) throw new Error('无法读取验证会话，请返回播放器重试。');
    const config = await response.json();
    // Only device identifiers are exposed to the browser, never the account token.
    for (const [name, value] of Object.entries({ mid: config.mid, KUGOU_API_MID: config.mid, dfid: config.dfid, userid: config.userid })) {
      document.cookie = `${name}=${encodeURIComponent(value)}; Path=${location.pathname}; SameSite=Strict`;
    }
    await wasm_bindgen('verifycode_bg_ios.wasm');
    wasm_bindgen.run();
    eData = new wasm_bindgen.EData();
    if (config.type === 23) {
      await new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://turing.captcha.qcloud.com/TCaptcha.js';
        script.onload = resolve;
        script.onerror = () => reject(new Error('腾讯验证码加载失败，请检查网络后刷新页面。'));
        document.head.append(script);
      });
    } else {
      document.getElementById('sms').hidden = false;
      verify.textContent = '提交验证码';
    }
    if (finished) return;
    verify.disabled = false;
    status.textContent = config.type === 23 ? '点击下方按钮完成验证码。' : '请输入安全验证短信中的验证码。';

    const submit = async verifyCode => {
      if (finished || busy) return;
      busy = true;
      verify.disabled = true;
      status.textContent = '正在确认验证结果…';
      try {
        const result = await post('submit', { verifyCode, sid: eData.get_sid(), edt: eData.get_edt() });
        if (finished) return;
        status.textContent = result.message;
        finished = result.ok === true;
        cancel.disabled = finished;
      } catch (error) {
        if (!finished) status.textContent = error.message || '提交失败，请返回播放器重试。';
      } finally {
        busy = false;
        verify.disabled = finished;
      }
    };
    verify.onclick = () => {
      if (config.type === 32) {
        const code = document.getElementById('code').value.trim();
        if (code) void submit(code);
        else status.textContent = '请输入验证码。';
        return;
      }
      verify.disabled = true;
      try {
        const captcha = new TencentCaptcha(config.appId, result => {
          if (finished) return;
          verify.disabled = false;
          if (result.ret === 0) {
            void submit('KGCodeTX|' + JSON.stringify({ ticket: result.ticket, randstr: result.randstr, txappid: config.appId }));
          } else {
            status.textContent = '未完成验证，可以重试或取消。';
          }
        }, { showHeader: false });
        captcha.show();
      } catch {
        verify.disabled = false;
        status.textContent = '无法启动腾讯验证码，请刷新页面重试。';
      }
    };
  } catch (error) {
    if (!finished) status.textContent = error.message || '验证初始化失败，请返回播放器重试。';
  }
})();
