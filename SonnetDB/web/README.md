# SonnetDB Admin UI

基于 **Vite + Vue 3 + TypeScript + Naive UI + Pinia + Vue Router** 的单页应用。

当前采用与 ASP.NET Core 官方 SPA 模板一致的关系：

- 调试期：`src/SonnetDB` 通过 `SpaProxy` 自动执行 `npm run dev`，浏览器进入 `https://localhost:5173/admin/`。
- 发布期：`web/dist` 作为 Static Web Assets 输出到服务端 `wwwroot/admin/`，由 ASP.NET Core 以静态文件方式托管。

## 本地开发

```bash
cd web
npm install
npm run dev
```

也可以直接调试后端项目：

```bash
dotnet run --project src/SonnetDB --launch-profile SonnetDB
```

这时 `SpaProxy` 会自动启动前端开发服务器，Vite 会把 `/v1`、`/healthz`、`/metrics`、`/help`、`/mcp` 代理到 ASP.NET Core。

## 生产构建

```bash
cd web
npm install
npm run build
```

或直接：

```bash
dotnet publish src/SonnetDB/SonnetDB.csproj -c Release
```

发布时 `web` 项目会自动执行前端构建，产物会放到服务端静态资源目录并保留 `/admin/` 路由前缀。

## 设计要点

- 控制面管理动作通过 SQL 端点完成：admin 走 `POST /v1/sql` 执行 `CREATE USER` / `GRANT` / `ISSUE TOKEN` 等；数据面 SQL 走 `POST /v1/db/{db}/sql`。
- 数据库列表与状态展示复用 `GET /v1/db` 和 `GET /metrics`，这样普通已登录用户也能查看数据库概览，而不必依赖 admin-only 控制面端点。
- 认证：`POST /v1/auth/login` 拿 token → 存 localStorage → axios 拦截器自动加 `Bearer`。
- 路由前缀：`/admin/`，生产环境通过 `MapStaticAssets + MapFallbackToFile` 提供 SPA fallback。
