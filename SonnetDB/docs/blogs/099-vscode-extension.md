## SonnetDB VS Code 扩展预览：连接管理、SQL 编辑器与结果可视化

为提升开发者体验，SonnetDB 团队正在开发 Visual Studio Code 扩展，将数据库管理能力直接嵌入到开发者的日常编辑环境中。本文将预览扩展的核心功能：**连接树**、**SQL 编辑器**和**查询结果视图**，带您一窥即将发布的开发者工具。

### 连接管理树（Connection Tree）

VS Code 扩展的活动栏中集成了 SonnetDB 面板，以树形结构展示所有数据库连接。每个连接节点下按 Measurement 组织，展开后可看到 Tag、Field 列定义以及数据预览。

```
SONNETDB
├── 本地开发 (localhost:3260)
│   ├── cpu
│   │   ├── Tag: host          ← 右键可查看数据分布
│   │   ├── Field: usage (FLOAT)
│   │   └── Field: temperature (FLOAT)
│   ├── sensor
│   │   ├── Tag: device_id
│   │   ├── Tag: region
│   │   └── Field: temperature (FLOAT)
│   └── power
│       ├── Tag: station
│       ├── Field: voltage (FLOAT)
│       └── Field: current (FLOAT)
├── 生产环境 (prod:3260)
│   ├── cpu
│   └── ...
└── [添加新连接...]
```

支持同时管理多个连接，右键菜单提供快速操作入口，包括新建查询、复制连接字符串、查看 Measurement 属性等。

### SQL 编辑器增强

扩展集成了针对 SonnetDB 语法优化的 SQL 编辑器，提供智能提示、语法高亮和自动补全：

```sql
-- 在 VS Code 中编辑 SonnetDB SQL
-- 输入 "CREATE" 自动提示 Measurement 语法
-- 输入表名自动提示列名

CREATE MEASUREMENT cpu (
    host TAG,          ← 输入 TAG 类型时高亮
    usage FIELD FLOAT, ← 输入 FLOAT 时自动提示数字类型
    temperature FIELD FLOAT
);

-- Ctrl+Space 智能补全
-- 输入 "SELECT * FROM cpu WHERE" 后，自动提示 host、usage、temperature
SELECT *
FROM cpu
WHERE host = 'server-01'
  AND time >= 1713676800000
ORDER BY time DESC
LIMIT 100;
```

编辑器功能包含：

- **语法高亮**：关键字、数据类型、内置函数使用不同的颜色标记
- **智能补全**：根据上下文自动提示 Measurement 名称、列名、函数和关键字
- **代码片段**：预设 CREATE MEASUREMENT、INSERT、SELECT 等常用模板
- **实时校验**：在编辑器中标记语法错误，无需提交到服务器即可发现问题

### 查询结果视图

执行查询后，结果在编辑器下方以表格形式展示，支持丰富的交互操作：

```typescript
// 扩展的查询结果面板特性
interface QueryResultView {
    // 表格渲染：支持百万行级别的虚拟滚动
    virtualScrolling: true;

    // 导出格式
    exportFormats: ['CSV', 'JSON', 'Parquet', 'Excel'];

    // 结果交互
    features: [
        '列排序（点击表头）',
        '列宽拖拽调整',
        '选中行复制（Ctrl+C）',
        '实时筛选（按列值过滤）',
        '结果统计（选中范围自动显示 SUM/AVG/COUNT）',
        '图表快速生成（选中数据一键转为折线图/柱状图）'
    ];
}
```

时序数据的结果集还可以一键切换为图表模式。对于包含时间列和数值列的结果集，扩展自动推断并生成折线图，支持缩放、平移和数据点查看：

```
┌──────────────────────────────────────────┐
│  图表模式: host = server-01 最近 24h    │
│  ┌────────────────────────────────────┐  │
│  │ 0.98 ┤            ▄▄▄             │  │
│  │ 0.75 ┤    ▄▄▄   ▄    ▀▀▀  ▄▄▄    │  │
│  │ 0.50 ┤  ▄     ▀▀        ▄    ▀▀  │  │
│  │ 0.25 ┤ ▀                       ▀  │  │
│  └────────────────────────────────────┘  │
│  12:00    18:00    00:00    06:00    12:00 │
└──────────────────────────────────────────┘
```

### 扩展配置

VS Code 扩展的配置非常简单，在设置中填写 SonnetDB 连接信息即可：

```json
// settings.json
{
  "sonnetdb.connections": [
    {
      "name": "本地开发",
      "host": "localhost",
      "port": 3260,
      "database": "mydb",
      "authToken": "",
      "useTls": false
    }
  ],
  "sonnetdb.query.limitDefault": 500,
  "sonnetdb.query.confirmBeforeDrop": true,
  "sonnetdb.results.chartDefaultType": "line"
}
```

SonnetDB VS Code 扩展目前处于预览阶段（M18 里程碑），欢迎社区贡献者参与开发和测试。通过将数据库操作无缝集成到开发环境，SonnetDB 致力于为开发者提供从编码到数据查询的一致体验。
