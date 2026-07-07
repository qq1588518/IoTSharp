<h1>
  <img src="assets/jsondb-logo-64.png" alt="IoTSharp.Data.JsonDB logo" width="48" height="48" align="absmiddle" />
  IoTSharp.Data.JsonDB
</h1>

IoTSharp.Data.JsonDB is a lightweight in-memory relational database engine that executes SQL queries over JSON data through a full ADO.NET interface.

## Features

- SQL `SELECT`, `INSERT`, `UPDATE`, `DELETE` over JSON arrays and objects
- `WHERE`, `GROUP BY`, `HAVING`, `ORDER BY` with `asc`, `desc`, `ascnum`, `descnum`, and `LIMIT`
- SQLite-style basic aggregate functions: `COUNT`, `SUM`, `TOTAL`, `AVG`, `MIN`, `MAX`, `GROUP_CONCAT`, `STRING_AGG`
- Arithmetic and boolean expressions with custom function registration
- Path-style field access such as `profile.name` and `metrics.score`
- Standard ADO.NET types: `DbConnection`, `DbCommand`, `DbDataReader`, `DbParameter`, `DbDataAdapter`
- Parameterized queries with `@paramName`
- JSON file, JSON string, and `System.Text.Json.Nodes.JsonNode` data sources
- File-backed auto-save for `INSERT`, `UPDATE`, and `DELETE`

## Install

```powershell
dotnet add package IoTSharp.Data.JsonDB
```

## Quick Start

```csharp
using IoTSharp.Data.JsonDB;

var json = """[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]""";
using var conn = JsonDbConnection.FromJson(json);
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT name FROM input WHERE id = @id";
cmd.Parameters.AddWithValue("@id", 1);

var name = cmd.ExecuteScalar();
Console.WriteLine(name); // Alice
```

## File-Backed JSON

```csharp
using var conn = new JsonDbConnection("Data Source=data.json");
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = "UPDATE input SET status = \"done\" WHERE id = 1";
cmd.ExecuteNonQuery();
```

File-backed connections save mutations automatically by default. Set `conn.AutoSave = false` and call `conn.SaveToFile()` when manual persistence is preferred.

## Provider Factory

```csharp
var factory = JsonDbProviderFactory.Instance;
using var conn = (JsonDbConnection)factory.CreateConnection()!;
conn.ConnectionString = "Data Source=data.json";
conn.Open();
```

## SQL Dialect

```sql
SELECT field1, field2 AS alias
FROM input
WHERE status = "active"
ORDER BY score DESCNUM
LIMIT 0, 10

SELECT category, COUNT(*) AS count, AVG(score) AS averageScore
FROM input
GROUP BY category
HAVING count > 1
ORDER BY averageScore DESCNUM

INSERT INTO input SET id = 3, name = "Cora"
UPDATE input SET score = score + 1 WHERE id = 3
DELETE FROM input WHERE id = 3
```

## Build

```powershell
dotnet restore Data.JsonDB.slnx
dotnet test Data.JsonDB.slnx -c Release
dotnet pack src/IoTSharp.Data.JsonDB/IoTSharp.Data.JsonDB.csproj -c Release
```

NuGet publishing is handled by GitHub Actions when a tag starting with `v` is pushed, for example `v1.0.0`.

## Logo Assets

- `assets/jsondb-logo-32.png`
- `assets/jsondb-logo-48.png`
- `assets/jsondb-logo-64.png`
- `assets/jsondb-logo-128.png`
- `assets/jsondb-logo-256.png`
- `assets/jsondb-logo-512.png`
- `assets/jsondb-logo-source.png`
