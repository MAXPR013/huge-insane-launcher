// HIL Huge 隧道中继（独立 Worker：hgl-relay）
// GET /api/relay?room=XXXXXX&role=host|join，WebSocket 升级；房间状态由 Durable Object 持有。
// 控制帧：文本 JSON（{"type":"ok"|"error"|"peer"|"close"|"ka"}）。
// 数据帧：二进制。玩家侧 [u16 session][payload]；腐竹侧 [u16 joiner][u16 session][payload]。
const MAX_JOINERS = 8;           // 每个隧道房间最多玩家数
const ROOM_RE = /^[A-Z2-9]{6}$/; // 房间码：6 位无歧义字符（无 0/O/1/I/L）

function json(obj, status) {
  return new Response(JSON.stringify(obj), {
    status: status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}

async function handleRelay(request, env) {
  if (request.method !== "GET") return json({ error: { message: "method not allowed" } }, 405);
  const ua = request.headers.get("User-Agent") || "";
  if (ua.indexOf("HugeInsaneLauncher") < 0 && ua.indexOf("HugeGoingLauncher") < 0)
    return json({ error: { message: "forbidden" } }, 403);
  if (request.headers.get("Upgrade") !== "websocket")
    return json({ error: { message: "expected websocket" } }, 426);

  const url = new URL(request.url);
  const room = (url.searchParams.get("room") || "").toUpperCase();
  const role = url.searchParams.get("role") || "";
  if (!ROOM_RE.test(room)) return json({ error: { message: "房间码格式错误" } }, 400);
  if (role !== "host" && role !== "join") return json({ error: { message: "角色错误" } }, 400);

  const id = env.RELAY_ROOM.idFromName(room);
  const stub = env.RELAY_ROOM.get(id);
  return stub.fetch(request);
}

// 房间 DO：每房间一个实例。WebSocket Hibernation API（ctx.acceptWebSocket），
// 空闲时不计费不驻留内存；角色/编号存附件，唤醒后还原。
export class RelayRoom {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    const role = url.searchParams.get("role");
    const ip = request.headers.get("CF-Connecting-IP") || "";   // 打洞信令用：告诉对方你的公网 IP
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];

    if (role === "host") {
      const old = this.host();
      if (old) {
        // 同码已有腐竹：按僵尸连接处理，踢掉旧的（新连进来的优先），让玩家重进
        try { old.send(JSON.stringify({ type: "error", message: "同一房间码在别处重新注册，本连接被顶替" })); } catch (e) {}
        try { old.close(4000, "replaced"); } catch (e) {}
        for (const ws of this.joiners()) this.safeError(ws, "腐竹重新上线，请重新加入");
      }
      this.state.acceptWebSocket(server, ["host"]);
      server.serializeAttachment({ role: "host", ip: ip });
      server.send(JSON.stringify({ type: "ok", role: "host", ip: ip }));
    } else {
      const host = this.host();
      if (!host) {
        server.accept();
        server.send(JSON.stringify({ type: "error", message: "房间不存在或腐竹已离线" }));
        server.close(4004, "no host");
      } else {
        const joiners = this.joiners();
        if (joiners.length >= MAX_JOINERS) {
          server.accept();
          server.send(JSON.stringify({ type: "error", message: "房间已满（最多 " + MAX_JOINERS + " 人）" }));
          server.close(4009, "full");
        } else {
          const used = new Set();
          for (const ws of joiners) used.add(ws.deserializeAttachment().id);
          let jid = 1;
          while (used.has(jid)) jid++;
          this.state.acceptWebSocket(server, ["join"]);
          server.serializeAttachment({ role: "join", id: jid, ip: ip });
          // 把腐竹的公网 IP 带给玩家（打洞目标）
          let hostIp = "";
          try { hostIp = host.deserializeAttachment().ip || ""; } catch (e) {}
          server.send(JSON.stringify({ type: "ok", role: "join", id: jid, hip: hostIp }));
          try { host.send(JSON.stringify({ type: "peer", j: jid, action: "join", ip: ip })); } catch (e) {}
        }
      }
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    const meta = ws.deserializeAttachment();
    if (!meta) return;
    if (typeof message === "string") {
      let ctl;
      try { ctl = JSON.parse(message); } catch (e) { return; }
      if (ctl.type === "ka") { try { ws.send(JSON.stringify({ type: "ka" })); } catch (e) {} return; }
      if (ctl.type === "close") {
        if (meta.role === "join") {
          const host = this.host();
          if (host) { try { host.send(JSON.stringify({ type: "close", j: meta.id, s: ctl.s })); } catch (e) {} }
        } else if (meta.role === "host") {
          const target = this.joiner(ctl.j);
          if (target) { try { target.send(JSON.stringify({ type: "close", s: ctl.s })); } catch (e) {} }
        }
      }
      // 打洞信令透传：玩家→腐竹补上 j；腐竹→指定玩家
      if (ctl.type === "sig") {
        if (meta.role === "join") {
          const host = this.host();
          if (host) { try { ctl.j = meta.id; host.send(JSON.stringify(ctl)); } catch (e) {} }
        } else if (meta.role === "host") {
          const target = this.joiner(ctl.j);
          if (target) { try { delete ctl.j; target.send(JSON.stringify(ctl)); } catch (e) {} }
        }
      }
      return;
    }
    // 二进制数据帧
    const bytes = new Uint8Array(message);
    if (bytes.length < 2) return;
    if (meta.role === "join") {
      const host = this.host();
      if (!host) return;
      const out = new Uint8Array(bytes.length + 2);
      out[0] = meta.id >> 8; out[1] = meta.id & 0xFF;
      out.set(bytes, 2);
      try { host.send(out); } catch (e) {}
    } else {
      const jid = (bytes[0] << 8) | bytes[1];
      const target = this.joiner(jid);
      if (!target) return;
      try { target.send(bytes.subarray(2)); } catch (e) {}
    }
  }

  async webSocketClose(ws) { this.onGone(ws); }
  async webSocketError(ws) { this.onGone(ws); }

  onGone(ws) {
    let meta = null;
    try { meta = ws.deserializeAttachment(); } catch (e) {}
    if (!meta) return;
    if (meta.role === "host") {
      for (const j of this.joiners()) this.safeError(j, "腐竹关闭了隧道");
    } else {
      const host = this.host();
      if (host) { try { host.send(JSON.stringify({ type: "peer", j: meta.id, action: "leave" })); } catch (e) {} }
    }
    try { ws.close(1000); } catch (e) {}
  }

  safeError(ws, msg) {
    try { ws.send(JSON.stringify({ type: "error", message: msg })); } catch (e) {}
    try { ws.close(4001, msg); } catch (e) {}
  }

  host() {
    const list = this.state.getWebSockets("host");
    return list.length > 0 ? list[0] : null;
  }
  joiners() { return this.state.getWebSockets("join"); }
  joiner(jid) {
    for (const ws of this.joiners()) {
      const m = ws.deserializeAttachment();
      if (m && m.id === jid) return ws;
    }
    return null;
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/api/relay") return handleRelay(request, env);
    return json({ error: { message: "hgl-relay: Huge 隧道中继运行中" } }, 200);
  },
};
