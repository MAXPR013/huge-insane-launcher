// HIL Modrinth 反代 Worker（Cloudflare Workers 免费档够用）
// 路由约定（与 HugeLauncher Modrinth.MirrorOverride 对应）：
//   {worker}/v2/*  → https://api.modrinth.com/v2/*   （响应里的 cdn 链接重写回本 worker）
//   {worker}/cdn/* → https://cdn.modrinth.com/*
// 部署：wrangler deploy 或 Cloudflare Dashboard → Workers → 新建 → 粘贴本文件
// 然后在 HIL 启动器设置里把「Modrinth 镜像」填成 https://你的worker域名

const API = "https://api.modrinth.com";
const CDN = "https://cdn.modrinth.com";

export default {
  async fetch(request) {
    const url = new URL(request.url);

    if (url.pathname.startsWith("/cdn/")) {
      // 文件 CDN 直通流式转发
      return fetch(CDN + url.pathname.slice(4) + url.search, {
        headers: { "User-Agent": "HugeInsaneLauncher/3.0" },
      });
    }

    if (url.pathname.startsWith("/v2/")) {
      const resp = await fetch(API + url.pathname + url.search, {
        headers: { "User-Agent": "HugeInsaneLauncher/3.0" },
      });
      // JSON 响应里的 cdn.modrinth.com 链接重写为 worker /cdn/，让文件下载也走反代
      const ct = resp.headers.get("content-type") || "";
      if (!ct.includes("json")) return resp;
      let text = await resp.text();
      const origin = url.origin;
      text = text.split("https://cdn.modrinth.com/").join(origin + "/cdn/");
      return new Response(text, {
        status: resp.status,
        headers: { "Content-Type": ct, "Access-Control-Allow-Origin": "*" },
      });
    }

    return new Response("HIL Modrinth Proxy: /v2/* → api.modrinth.com, /cdn/* → cdn.modrinth.com", { status: 200 });
  },
};
