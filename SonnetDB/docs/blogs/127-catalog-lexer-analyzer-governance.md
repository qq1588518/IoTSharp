# 读多写少结构治理：FrozenDictionary、Lexer 快路径与 Analyzer

性能优化不只发生在存储和查询执行器里。Catalog、schema、tag index、lexer 和 analyzer 配置同样会影响长期维护成本。

## Frozen snapshot 适合什么

SonnetDB 有一些结构在启动后读多写少：

- series catalog
- measurement catalog
- measurement schema 列名索引
- tag inverted index

这类结构适合写入时构建新快照，然后原子替换给读路径。读路径拿到的快照不可变，可以无锁查询。

这轮改动把相关字典替换为 `FrozenDictionary` / `FrozenSet` 发布快照，同时保留写入 builder。测试覆盖查找正确性、更新后可见性和并发读写。

## Options/config 值对象化

配置对象如果在运行时被共享并修改，会带来并发不确定性。现在多类 options/config 改为 `sealed record` 和 init-only 属性，仍兼容：

```csharp
var options = new TsdbOptions
{
    RootDirectory = path,
};
```

同时也支持 `with` 创建新快照：

```csharp
var adjusted = options with { UseSimdNumericAggregates = false };
```

## SQL Lexer 的 SearchValues 快路径

SQL Lexer 的空白、标识符、数字、duration 后缀与运算符判断改为 `SearchValues<char>` ASCII 快路径，同时保留 Unicode 标识符和空白 fallback。

这类优化适合 lexer：字符分类调用极其频繁，减少分支可以稳定降低解析成本。新增的 `SqlLexerBenchmark` 用于对比旧分支分类 lexer 与新实现。

## Analyzer 作为护栏

`SonnetDB.Core` 增加性能相关 analyzer 配置，覆盖：

- 热路径 LINQ
- 重复数组分配
- `Count` / `Any`
- Dictionary 查询
- `SearchValues`
- 字符串比较

低噪声规则设为 warning，语义取舍较多的规则先作为 suggestion。新增 warning 命中点没有 suppress，而是通过代码修复：保留字符搜索缓存 `SearchValues`，SQL 元数据结果列名复用只读集合。

这套治理的意义是把“以后别再写回慢路径”变成编译期反馈。
