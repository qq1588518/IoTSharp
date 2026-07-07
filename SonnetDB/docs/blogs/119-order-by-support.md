## ORDER BY 排序：让时序查询结果井然有序

排序是 SQL 查询中最基础也最常用的子句之一。对于时序数据库而言，时间排序更是一个高频需求——查看最新的数据点、按时间降序排列事件日志、获取最早或最晚的 N 条记录。本次更新为 SonnetDB 带来了 **ORDER BY** 支持，填补了 SQL 语法体系中一块重要的拼图。

### 语法设计：从简出发

SonnetDB 的 ORDER BY 语法遵循 SQL 标准，采用 `ORDER BY column [ASC | DESC]` 的基本形式：

```sql
-- 按时间升序排列（默认）
SELECT time, host, usage
FROM cpu
WHERE host = 'edge-1'
ORDER BY time ASC
LIMIT 10;

-- 按时间降序排列
SELECT time, host, usage
FROM cpu
WHERE host = 'edge-1'
ORDER BY time DESC
LIMIT 10;

-- ASC 为默认方向，可省略
SELECT time, usage FROM cpu ORDER BY time LIMIT 10;
```

需要说明的是，当前版本仅支持 `ORDER BY time`。这并非技术限制，而是一个审慎的起点——在时序数据库中，时间列是天然的主排序键。随着引擎的持续演进，对其他列的排序支持将在后续版本中逐步放开。

### 内部实现：贯穿 SQL 全链路

ORDER BY 的实现横跨了 SonnetDB 的整个 SQL 处理管线，涉及词法分析、语法解析、AST 抽象和执行引擎四个层次。

**词法分析阶段**，`SqlLexer` 新增了 `ORDER` 和 `ASC` 关键字识别。值得留意的是 `DESC` 关键字的复用——它最初是为 `DESCRIBE` 命令（元数据查询）引入的，在 ORDER BY 语境下被赋予了"降序排列"的新语义，避免了关键字的冗余定义。

**语法解析阶段**，`SqlParser` 在 SELECT 语句解析流程中插入了 `ParseOptionalOrderBy` 方法。该方法在解析完 FROM 和 WHERE 子句之后、解析 LIMIT/OFFSET 之前被调用，确保了排序子句在 SQL 语法树中的正确位置。解析器会验证排序列必须是 `time`，对于其他列的引用会抛出明确的错误提示。

**AST 层面**，`SelectStatement` 新增了 `OrderBySpec?` 可选属性。`OrderBySpec` 是一个不可变记录类型，包含两个字段：`SqlExpression`（排序所依据的表达式）和 `SortDirection`（升序或降序的枚举值）。

**执行引擎**才是真正的核心。`SelectExecutor` 在执行查询时，先调用 `ApplyOrderBy` 对结果集排序，再调用 `ApplyPagination` 进行分页——这个顺序保证了 `ORDER BY ... LIMIT N` 返回的确实是排序后的前 N 行，而非先截断再排序。

```csharp
// 执行器中的关键流程（简化示意）
return ApplyPagination(
    ApplyOrderBy(result, statement.OrderBy),  // 先排序
    statement.Pagination);                      // 后分页
```

排序的具体实现利用 LINQ 的 `OrderBy` / `OrderByDescending` 对内存中的结果行进行排序。时间戳值通过 `RequireOrderByTimestamp` 辅助方法提取，该方法支持 `long`（Unix 毫秒时间戳）和 `DateTime` 两种输入类型，并对无法识别的时间值抛出异常。

### 使用注意事项

**SELECT 中必须包含 time 列**。因为排序目前仅支持 time 列，如果查询投影中没有 time，执行器会抛出 `InvalidOperationException` 并提示"ORDER BY time 要求 SELECT 结果中包含 time 列"。

**排序与分页的配合**。`ORDER BY` 和 `LIMIT` 的组合是最常见的用法，SonnetDB 正确处理了二者的执行顺序，确保"最新的 10 条"或"最早的 5 个数据点"这类查询的语义正确性。

### 小结

ORDER BY 的实现虽然是 SQL 语法体系中的一小步，但它打通了词法→语法→AST→执行的完整链路，为后续更多的 SQL 标准特性（GROUP BY、HAVING、多列排序）奠定了坚实的基础架构。对于使用者而言，现在可以用标准 SQL 的方式对时序数据排序，查询表达力得到了进一步提升。
