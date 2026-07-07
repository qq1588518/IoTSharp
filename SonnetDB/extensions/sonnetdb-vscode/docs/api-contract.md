# API Contract Reuse

This document maps SonnetDB server endpoints to VS Code extension features.

## Existing endpoints to reuse

| Endpoint | Extension usage |
|----------|-----------------|
| `GET /v1/db` | connection health, database list, explorer roots |
| `GET /v1/db/{db}/schema` | schema explorer, SQL completion seed data |
| `POST /v1/db/{db}/sql` | query execution, DDL and DML entry point |
| `POST /v1/db/{db}/measurements/{m}/lp` | line protocol import |
| `POST /v1/db/{db}/measurements/{m}/json` | JSON points import |
| `POST /v1/db/{db}/measurements/{m}/bulk` | bulk VALUES import |
| `POST /v1/copilot/chat/stream` | Copilot panel event stream |
| `GET /v1/copilot/models` | Copilot model picker |
| `GET /v1/copilot/knowledge/status` | Copilot readiness / info card |
| `GET /healthz` | connection probe |
| `GET /v1/setup/status` | detect first-run setup state |
| `/mcp/{db}` | future VS Code agent and MCP integration |

## Reuse from existing web admin code

The VS Code extension should copy or adapt these ideas from the current web admin:

- NDJSON result parsing from `web/src/api/sql.ts`
- schema loading from `web/src/api/schema.ts`
- SonnetDB SQL dialect keywords from `web/src/components/sonnetdb-dialect.ts`
- chart heuristics from `web/src/components/SqlResultChart.vue`
- Copilot request shape from `web/src/api/copilot.ts`

## Data models worth mirroring in the extension

- `SqlResultSet`
- `SchemaResponse`
- `MeasurementInfo`
- `ColumnInfo`
- `CopilotChatRequest`
- `CopilotChatEvent`

## Optional future endpoints

The first extension wave can ship without server changes.

Possible future additions if product gaps appear:

- a compact `sample_rows` REST endpoint for explorer previews
- query-history persistence endpoints
- a dedicated explain endpoint for editor diagnostics
- local-runtime bootstrap endpoints for extension-managed setup flows
