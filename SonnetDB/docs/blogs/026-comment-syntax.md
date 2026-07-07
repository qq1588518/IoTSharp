## 注释语法：SonnetDB SQL 支持的四种注释方式

良好的注释习惯能够显著提升 SQL 脚本的可读性和可维护性。SonnetDB 兼容了四种不同的注释风格，覆盖了从 SQL 标准到编程语言的常见习惯，让你可以在 SQL 脚本中使用自己最熟悉的方式添加注释。

### 风格一：双连字符 `--`

这是最经典的 SQL 注释风格，在 ANSI SQL 标准中定义。从 `--` 开始到行尾的所有内容都被视为注释：

```sql
-- 查询 CPU 使用率超过 0.8 的高负载记录
SELECT time, host, usage
FROM cpu
WHERE usage > 0.8;  -- 过滤条件

-- 多行注释需要每行都加 --
-- 这是第二行注释
-- 这是第三行注释
```

### 风格二：C 风格双斜线 `//`

这种风格源自 C/C++ 和 Java 等编程语言，在 SonnetDB 中同样支持：

```sql
// 查询内存使用情况
SELECT time, host, used, total
FROM mem
WHERE host = 'server-01';

SELECT avg(usage) AS avg_usage  // 计算平均使用率
FROM cpu;
```

`//` 和 `--` 一样是单行注释，从 `//` 开始到行尾的内容都会被忽略。对于习惯现代编程语言风格的开发者来说，这种注释方式可能更加顺手。

### 风格三：C 风格块注释 `/* */`

当需要跨多行注释时，块注释是最方便的选择。它借鉴自 C 语言，以 `/*` 开头，以 `*/` 结尾：

```sql
/*
 * CPU 监控查询
 * 用途：获取 server-01 在指定时间段的 CPU 使用率
 * 日期：2024-04-21
 */
SELECT time, host, usage
FROM cpu
WHERE host = 'server-01'
  AND time >= 1713657600000;

/* 行内注释 */ SELECT count(*) FROM cpu;
```

块注释的一个妙用是快速注释掉 SQL 中的部分内容以便调试：

```sql
SELECT time, host, usage, cores
  --, throttled  /* 临时注释掉此列 */
FROM cpu
WHERE host = 'server-01';
```

### 风格四：BASIC 风格 REM

REM（Remark 的缩写）是一种怀旧风格的注释，源自 BASIC 语言：

```sql
REM 查询 server-02 的数据
SELECT time, host, usage
FROM cpu
WHERE host = 'server-02';
```

### 注释的最佳实践

在实际编写 SQL 脚本时，建议遵循以下原则：

- **文件头部注释**：使用 `/* */` 块注释说明脚本的整体用途、作者和日期
- **SQL 段注释**：使用 `--` 或 `//` 在关键查询前添加简短说明
- **行尾注释**：使用 `--` 在复杂表达式的行尾解释计算逻辑
- **调试注释**：使用 `/* */` 临时禁用某个字段或条件

四种注释风格在功能上是完全等价的，选择哪种主要取决于个人偏好和团队规范。SonnetDB 的 SQL 解析器会一致地处理它们，确保注释不会影响查询的执行结果。充分利用这些注释语法，可以让你的 SQL 脚本更具可读性和可维护性。
