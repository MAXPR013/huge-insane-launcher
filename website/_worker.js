// HIL 官网 Worker（_worker.js 高级模式：Pages / Workers 静态资产通用）
// 拦截 POST /api/hshi  → 转发智谱 GLM-4-Flash（key 藏于环境变量 GLM_KEY）；
// 拦截 GET  /api/relay → Huge 隧道：经 Workers 服务绑定 RELAY_SVC 转给 hgl-relay
//   （workers.dev 域名在国内被 DNS 污染 + SNI 重置，客户端只认本站域名）；
// 其余请求原样回退给静态资产（env.ASSETS）。
const UPSTREAM = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
const DAILY_LIMIT = 40;          // 每 IP 每天免费对话轮次
const MAX_BODY = 64 * 1024;      // 请求体上限 64KB（H史正常一轮 ~10KB）
const MAX_HISTORY = 24;          // 最多携带的消息条数（与 HIL 的 20 条历史上限对齐）

// isolate 内弱限流桶（无 KV 的免费版只能如此，防小白不防黑客；
// 真正的硬上限由智谱侧并发/总量限制兜底）
const buckets = new Map();

function json(obj, status) {
  return new Response(JSON.stringify(obj), {
    status: status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}

async function handleHshi(request, env) {
  if (request.method !== "POST")
    return json({ error: { message: "method not allowed" } }, 405);

  // 只认 HIL 客户端（桌面程序无 Origin，UA 是第一道门；兼容旧版 HGL UA）
  const ua = request.headers.get("User-Agent") || "";
  if (ua.indexOf("HugeInsaneLauncher") < 0 && ua.indexOf("HugeGoingLauncher") < 0)
    return json({ error: { message: "forbidden" } }, 403);

  if (!env.GLM_KEY)
    return json({ error: { message: "AI 服务未配置（Worker 可见变量: [" + Object.keys(env).join(", ") + "]，需包含 GLM_KEY）" } }, 503);

  // 每 IP 每日限流
  const ip = request.headers.get("CF-Connecting-IP") || "unknown";
  const day = new Date().toISOString().slice(0, 10);
  const bucketKey = day + "|" + ip;
  const used = (buckets.get(bucketKey) || 0) + 1;
  buckets.set(bucketKey, used);
  if (buckets.size > 8000) buckets.clear(); // 防内存膨胀
  if (used > DAILY_LIMIT)
    return json({ error: { message: "今日免费对话额度已用完（" + DAILY_LIMIT + " 次/天），明天再来；或在设置里填入自己的 DeepSeek key 不受此限。" } }, 429);

  // 读请求体
  let body;
  try {
    const raw = await request.text();
    if (raw.length > MAX_BODY) return json({ error: { message: "请求体过大" } }, 413);
    body = JSON.parse(raw);
  } catch (e) {
    return json({ error: { message: "请求格式错误" } }, 400);
  }

  // 白名单重建转发体：模型强制 glm-4-flash，历史截断，max_tokens 封顶
  const messages = Array.isArray(body.messages) ? body.messages.slice(-MAX_HISTORY) : [];
  if (messages.length === 0) return json({ error: { message: "缺少消息" } }, 400);
  const payload = {
    model: "glm-4-flash",
    messages: messages,
    tool_choice: "auto",
    max_tokens: 800,
  };
  if (Array.isArray(body.tools) && body.tools.length > 0) payload.tools = body.tools;

  // 转发上游
  let resp;
  try {
    resp = await fetch(UPSTREAM, {
      method: "POST",
      headers: { "Authorization": "Bearer " + env.GLM_KEY, "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
  } catch (e) {
    return json({ error: { message: "AI 上游连接失败，请稍后再试" } }, 502);
  }
  const text = await resp.text();
  return new Response(text, {
    status: resp.status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}

// Huge 隧道入口：只验身份+转发，房间逻辑全在 hgl-relay Worker（DO 也在那边）
async function handleRelay(request, env) {
  if (request.method !== "GET") return json({ error: { message: "method not allowed" } }, 405);
  const ua = request.headers.get("User-Agent") || "";
  if (ua.indexOf("HugeInsaneLauncher") < 0 && ua.indexOf("HugeGoingLauncher") < 0)
    return json({ error: { message: "forbidden" } }, 403);
  if (request.headers.get("Upgrade") !== "websocket")
    return json({ error: { message: "expected websocket" } }, 426);
  if (!env.RELAY_SVC)
    return json({ error: { message: "隧道服务未配置（Pages 需添加 Workers 服务绑定：RELAY_SVC → hgl-relay）" } }, 503);
  return env.RELAY_SVC.fetch(request);
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/api/hshi") return handleHshi(request, env);
    if (url.pathname === "/api/relay") return handleRelay(request, env);
    // 其余请求交给静态资产（SPA 回退等项目配置不受影响）
    return env.ASSETS.fetch(request);
  },
};
