/**
 * Copilot 新手引导 / 提示词模板（M10）。
 *
 * 每张卡片对应一个常见使用场景。`routeKeys` 用于按当前路由过滤，
 * 不指定时对所有页面可见；按钮点击后 `prompt` 字段会被填入 Copilot 输入框。
 */
export interface CopilotStarter {
  /** 分类：建表 / 写入 / 聚合 / 向量 / PID / 排查 / 其他。 */
  readonly category: string;
  /** 卡片标题（按钮文本）。 */
  readonly title: string;
  /** 简短说明，鼠标悬停显示。 */
  readonly description: string;
  /** 点击后填入输入框的提示词。 */
  readonly prompt: string;
  /** 仅在指定路由 key 上显示；不指定 = 全部页面。 */
  readonly routeKeys?: readonly string[];
}

/** 全部启动模板，按场景分组排列。 */
export const COPILOT_STARTERS: readonly CopilotStarter[] = [
  // —— 建表 ——
  {
    category: '建表',
    title: '温度采集表',
    description: '建一张存储温度数据的 measurement，包含设备 tag 与 float 值列。',
    prompt:
      '帮我建一张存储温度数据的 measurement，名为 sensor_temperature。需要以下列：\n' +
      '- device_id：设备标识（TAG）\n' +
      '- location：安装位置（TAG）\n' +
      '- temperature：温度读数（FLOAT FIELD）\n' +
      '- humidity：湿度读数（FLOAT FIELD）\n' +
      '请给出 CREATE MEASUREMENT 语句，并解释 TAG 与 FIELD 的差异。',
  },
  {
    category: '建表',
    title: '向量索引表',
    description: '创建支持 knn 向量检索的 measurement（VECTOR(N) 列）。',
    prompt:
      '帮我建一张支持向量检索的 measurement，存储文本 embedding：\n' +
      '- doc_id：文档 ID（TAG）\n' +
      '- title：标题（STRING FIELD）\n' +
      '- embedding：768 维向量（VECTOR(768) FIELD）\n' +
      '建好后给出一条示例 INSERT 与一条 knn 查询。',
  },

  // —— 写入 ——
  {
    category: '写入',
    title: '批量 INSERT 模板',
    description: '生成多行批量 INSERT 的标准写法，并说明性能注意事项。',
    prompt: '我有 10000 条传感器数据需要写入 sensor_temperature，请给出批量 INSERT 的 SQL 模板，并解释一次提交多少行最合适。',
  },

  // —— 聚合 ——
  {
    category: '聚合',
    title: '按 1 分钟聚合',
    description: 'GROUP BY time(1m) 求平均值的标准写法。',
    prompt: "查询过去 24 小时内 sensor_temperature 表中 device_id='sensor-01' 的每分钟平均温度，请用 GROUP BY time(1m) 写法。",
    routeKeys: ['sql', 'dashboard'],
  },
  {
    category: '聚合',
    title: 'TopN + 时间窗口',
    description: '取最近 1 小时温度最高的 10 条原始采样点。',
    prompt: '查询最近 1 小时内 sensor_temperature 表温度最高的 10 条原始数据，按 temperature 降序排序。',
  },

  // —— 向量 ——
  {
    category: '向量',
    title: 'knn 向量检索',
    description: 'knn 函数的参数顺序与距离度量选择。',
    prompt: '解释 SonnetDB 的 knn 函数：参数顺序、可选的距离度量（cosine / l2 / dot）以及如何与 WHERE 子句组合做混合检索。给一个完整示例。',
  },

  // —— 预测 / PID ——
  {
    category: '预测',
    title: '时序预测 forecast',
    description: 'forecast 函数：指定 measurement、值列与预测步长。',
    prompt: "使用 forecast 对 sensor_temperature.temperature 做未来 30 分钟的预测，只看 device_id='sensor-01' 的数据。",
  },
  {
    category: 'PID',
    title: 'PID 自动调参',
    description: 'pid_estimate / pid_series 用法与典型场景。',
    prompt: '我有一个温度控制场景，pv 来自 sensor_temperature.temperature，setpoint=25。请说明如何用 pid_estimate 自动给出 kp/ki/kd，再用 pid_series 计算输出。',
  },

  // —— 排查 ——
  {
    category: '排查',
    title: '慢查询分析',
    description: '基于当前 SQL 给出索引/聚合/分区建议。',
    prompt: '我现在编辑器里这条 SQL 跑得很慢，请基于 SonnetDB 的存储模型分析瓶颈，并给出优化建议（时间裁剪、GROUP BY time(...)、LIMIT 等）。',
    routeKeys: ['sql'],
  },
  {
    category: '排查',
    title: '查看 measurement 结构',
    description: '列出当前数据库的 measurement 与 schema。',
    prompt: '列出当前数据库的所有 measurement，并展示每张表的 schema（列名、角色、数据类型）。',
    routeKeys: ['sql', 'databases', 'dashboard'],
  },
];

/**
 * 根据当前路由 key 过滤模板。
 * 优先返回该路由独有的卡片，再补全公共卡片，最多 6 张。
 */
export function pickStarters(routeKey: string | null | undefined, max = 6): CopilotStarter[] {
  const key = routeKey ?? '';
  const matched: CopilotStarter[] = [];
  const generic: CopilotStarter[] = [];
  for (const s of COPILOT_STARTERS) {
    if (s.routeKeys && s.routeKeys.length > 0) {
      if (s.routeKeys.includes(key)) matched.push(s);
    } else {
      generic.push(s);
    }
  }
  const out = [...matched, ...generic];
  return out.slice(0, max);
}
