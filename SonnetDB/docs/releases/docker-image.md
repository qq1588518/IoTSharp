---
layout: default
title: Docker 镜像
description: SonnetDB 的容器镜像发布、标签策略与启动方式。
permalink: /releases/docker-image/
---

`PR #39` 为仓库补齐了 `SonnetDB` 的 Docker 镜像自动发布流水线，目标仓库为：

- Docker Hub：`iotsharp/sonnetdb`
- GHCR：`ghcr.io/<owner>/sonnetdb`

镜像内包含：

- `SonnetDB`
- 管理后台前端
- `/help` 静态帮助站点
- 默认的 `/data` 数据目录挂载点

## 标签策略

- `latest`：`main` 分支最新成功构建
- `edge`：`main` 分支滚动标签
- `vX.Y.Z`：与 Git tag 对齐的版本标签
- `X.Y`：同次版本滚动标签
- `sha-<commit>`：便于回溯具体提交

## 启动方式

```bash
docker run --rm \
  -p 5080:5080 \
  -v ./sonnetdb-data:/data \
  iotsharp/sonnetdb:latest
```

或者使用 GHCR：

```bash
docker run --rm \
  -p 5080:5080 \
  -v ./sonnetdb-data:/data \
  ghcr.io/<owner>/sonnetdb:latest
```

启动后默认可访问：

- `http://127.0.0.1:5080/admin/`
- `http://127.0.0.1:5080/help/`
- `http://127.0.0.1:5080/healthz`
- `http://127.0.0.1:5080/metrics`

## 自动发布工作流

仓库中的 `.github/workflows/docker-publish.yml` 会在以下场景触发：

- `main` 分支上与服务端镜像相关的文件变更
- 推送 `v*` 版本标签
- 手动触发 `workflow_dispatch`

工作流会：

1. 构建 `src/SonnetDB/Dockerfile`
2. 推送到 GHCR
3. 在 Docker Hub Secrets 配置完成后同步推送到 `iotsharp/sonnetdb`
4. 生成 OCI labels 与版本标签
5. 通过 GitHub Actions Cache 复用 Docker 层缓存

## 仓库 Secrets

若要把镜像推送到 Docker Hub，需要在仓库中配置：

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

GHCR 默认使用 `GITHUB_TOKEN` 登录，无需额外密码，但需要仓库允许 Actions 写入 packages。
