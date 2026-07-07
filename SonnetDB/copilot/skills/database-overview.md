---
name: database-overview
description: 查看当前数据库里有什么：当用户说“当前这个数据库里有什么”“看看这个库”“当前库有哪些表/measurement/字段/结构”时，优先围绕当前 SQL Console 选项卡选中的数据库做结构盘点，先执行 SHOW MEASUREMENTS，再按需要执行 DESCRIBE MEASUREMENT <name>。
triggers:
  - 当前这个数据库里有什么
  - 当前数据库里有什么
  - 这个库里有什么
  - 当前库
  - 看看这个库
  - 数据库概览
  - SHOW MEASUREMENTS
  - DESCRIBE MEASUREMENT
requires_tools:
  - query_sql
  - list_measurements
  - describe_measurement
---

# 当前数据库概览

当用户只是想知道“当前库里有什么”时，不要先把问题继续缩小，也不要只回工具名。

优先流程：

1. 如果当前页面是 SQL Console，优先使用当前选项卡选中的数据库作为目标数据库。
2. 先执行：

```sql
SHOW MEASUREMENTS
```

3. 如果返回 0 行，直接告诉用户当前库还是空的。
4. 如果返回少量 measurement，先把 measurement 名称列表直接告诉用户。
5. 只有当用户继续追问某个 measurement 的字段时，或者当前问题已经明确指向某个 measurement，才继续执行：

```sql
DESCRIBE MEASUREMENT <name>
```

回答要求：

- 先给直接结论，例如“当前库里有 3 个 measurement：cpu、memory、events”。
- 不要只说“我调用了 list_measurements / describe_measurement”。
- 不要要求用户先自己指定数据库；如果页面上下文已经给出当前 SQL Console 的数据库，就直接用它。
- 如果数据库里 measurement 很多，只列出前几项并说明可以继续展开某个 measurement 的结构。

常见问法：

- 查一查当前这个数据库里有什么
- 看看这个库
- 当前库里有哪些表
- 帮我看一下当前数据库结构
- 这个数据库都有哪些 measurement
