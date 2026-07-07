---
layout: default
title: Server Bundle
description: 下载即运行的完整服务端发布包，包含后台、CLI、文档和默认配置。
permalink: /releases/server-bundle/
---

Server Bundle 面向“下载即启动”的部署场景，默认包含：

- `SonnetDB` 可执行文件
- 管理后台前端
- `/help` 帮助文档静态站点
- `SonnetDB.Cli`
- `SonnetDB.Core` / `SonnetDB` / `SonnetDB.Cli` NuGet 包
- 启动脚本与说明文档

## 启动方式

Windows:

```powershell
.\start-sonnetdb.cmd
```

Linux:

```bash
chmod +x ./start-sonnetdb.sh ./sndb
./start-sonnetdb.sh
```

## 常用访问地址

- 管理后台: `http://127.0.0.1:5080/admin/`
- 帮助中心: `http://127.0.0.1:5080/help/`
- 健康检查: `http://127.0.0.1:5080/healthz`
- 指标接口: `http://127.0.0.1:5080/metrics`

## 目录结构示意

```text
sonnetdb-full-<version>-<rid>/
├─ SonnetDB(.exe)
├─ appsettings.json
├─ cli/
├─ packages/
├─ docs/
├─ sonnetdb-data/
└─ start-sonnetdb.cmd|sh
```
