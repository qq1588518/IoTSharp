## 查询元数据：SHOW 与 DESCRIBE 的使用

在使用任何数据库时，了解当前有哪些表、表结构是怎样的，是最基本的操作。SonnetDB 提供了一系列 SHOW 和 DESCRIBE 命令来查询元数据，帮助你快速了解数据库的 Schema 信息。

### 查看数据库中的表

当你需要了解当前数据库中有哪些测量表（Measurement）时，可以使用 SHOW MEASUREMENTS 或等价的 SHOW TABLES：

```sql
SHOW MEASUREMENTS;
SHOW TABLES;  -- 等效语法
```

这两个命令返回当前数据库中所有 Measurement 的列表。每创建一个 Measurement，都会出现在这个列表中。SonnetDB 同时支持两种写法，照顾不同 SQL 方言用户的习惯。

### 查看表结构

一旦知道了有哪些表，下一步通常是查看特定表的结构。这时可以使用 DESCRIBE MEASUREMENT 命令：

```sql
DESCRIBE MEASUREMENT cpu;
```

输出会展示表的各列信息，包括列名、数据类型和角色（TAG 或 FIELD）。对于更复杂的表，例如包含向量索引的表，输出还会显示索引信息：

```sql
DESCRIBE MEASUREMENT doc_indexed;
```

SonnetDB 也支持 `DESC` 作为 `DESCRIBE` 的简写别名，方便习惯简洁语法的用户：

```sql
DESC cpu;
DESC reactor;
```

### 系统级元数据查询

除了表级别的元数据，SonnetDB 还提供了多个 SHOW 命令来查看系统和用户信息：

```sql
-- 查看所有数据库
SHOW DATABASES;

-- 查看所有用户
SHOW USERS;

-- 查看授权信息
SHOW GRANTS;
SHOW GRANTS FOR writer;

-- 查看 API Token
SHOW TOKENS;
SHOW TOKENS FOR writer;
```

这些命令主要用于服务器的管理面，需要超级用户权限才能执行。其中 TOKEN 相关的命令不会返回 Token 的明文值，只会展示 Token 的 ID、所属用户和创建时间等元信息。

### 元数据查询的最佳实践

在实际使用中，推荐的工作流程是：

1. 连接到数据库后，先执行 `SHOW MEASUREMENTS` 确认可用表
2. 对感兴趣的表执行 `DESCRIBE MEASUREMENT <name>` 查看字段定义
3. 根据字段类型和 TAG/FIELD 角色，编写精确的查询语句

```sql
USE demo;

-- 步骤 1：列出所有表
SHOW MEASUREMENTS;

-- 步骤 2：查看具体表结构
DESCRIBE MEASUREMENT cpu;

-- 步骤 3：基于表结构编写查询
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
  AND time >= 1713657600000;
```

这套元数据查询工具不仅适用于人工操作，也被 SonnetDB Copilot 广泛使用。当 Copilot 需要理解你的 Schema 时，它会自动调用 `SHOW MEASUREMENTS` 和 `DESCRIBE MEASUREMENT` 来获取上下文，从而生成更精准的 SQL 建议。
