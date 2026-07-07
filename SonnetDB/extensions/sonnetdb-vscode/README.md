# SonnetDB for VS Code

This directory contains the planning scaffold for the official SonnetDB VS Code extension.

The extension is intended to be:

- remote-first for the first production slice
- TypeScript-native in the VS Code extension host
- able to reuse the existing SonnetDB HTTP, schema, Copilot, and MCP contracts
- compatible with a later managed local-server mode for opening local data roots

## Current scope

This scaffold does not try to ship a full extension yet. It provides:

- a proposed directory layout
- a minimal extension manifest and TypeScript bootstrap
- placeholder commands, tree view, and webview panels
- a dedicated roadmap for the extension
- architecture and API-contract notes for future implementation work

## Directory layout

```text
extensions/sonnetdb-vscode/
├─ README.md
├─ ROADMAP.md
├─ package.json
├─ tsconfig.json
├─ .gitignore
├─ .vscodeignore
├─ media/
│  └─ sonnetdb.svg
├─ docs/
│  ├─ architecture.md
│  └─ api-contract.md
└─ src/
   ├─ extension.ts
   ├─ commands/
   │  └─ runQueryCommand.ts
   ├─ core/
   │  ├─ config.ts
   │  ├─ sonnetdbClient.ts
   │  └─ types.ts
   ├─ panels/
   │  ├─ copilotPanel.ts
   │  └─ queryResultPanel.ts
   └─ tree/
      └─ sonnetdbTreeDataProvider.ts
```

## Working principles

- Phase 1 reuses `POST /v1/db/{db}/sql`, `GET /v1/db/{db}/schema`, `GET /v1/db`, and `POST /v1/copilot/chat/stream`.
- Tokens should live in VS Code `SecretStorage`, not in plain-text workspace settings.
- The first local-mode implementation should start a managed SonnetDB server process for a selected data root instead of embedding the .NET engine directly into the Node extension host.
- The existing web admin code is the primary reference for NDJSON parsing, SQL dialect keywords, schema completion, chart rendering, and Copilot request payloads.

## First implementation wave

The recommended first wave is:

1. `#99` extension bootstrap and manifest
2. `#100` remote connection model and secret storage
3. `#101` explorer tree and schema refresh
4. `#102` SQL execution flow and query command
5. `#103` result panel with table, raw, and chart tabs

See [ROADMAP.md](./ROADMAP.md) for the detailed split.
