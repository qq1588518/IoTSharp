<template>
  <div class="sql-chart" v-if="chartReady">
    <div class="sql-chart__head">
      <n-space size="small" align="center" :wrap="true">
        <span class="sql-chart__label">时间列：</span>
        <n-select size="small" style="width: 140px" v-model:value="timeColumn" :options="timeOptions" />
        <span class="sql-chart__label">数值字段：</span>
        <n-select
          size="small"
          style="min-width: 200px; max-width: 360px"
          v-model:value="valueColumns"
          :options="valueOptions"
          multiple
          :max-tag-count="3"
        />
        <span class="sql-chart__label" v-if="tagOptions.length > 0">分组 tag：</span>
        <n-select
          v-if="tagOptions.length > 0"
          size="small"
          style="min-width: 160px; max-width: 240px"
          v-model:value="groupByTag"
          :options="tagOptions"
          clearable
          placeholder="不分组"
        />
      </n-space>
    </div>
    <div class="sql-chart__canvas">
      <svg
        :viewBox="`0 0 ${width} ${height}`"
        preserveAspectRatio="none"
        xmlns="http://www.w3.org/2000/svg"
        role="img"
        aria-label="SQL 结果折线图"
      >
        <!-- Y 轴网格线 + 标签 -->
        <g class="sql-chart__grid">
          <line
            v-for="(tick, i) in yTicks"
            :key="`yg${i}`"
            :x1="padLeft" :x2="width - padRight"
            :y1="tick.y" :y2="tick.y"
          />
          <text
            v-for="(tick, i) in yTicks"
            :key="`yl${i}`"
            :x="padLeft - 6" :y="tick.y + 4"
            text-anchor="end"
          >{{ formatTick(tick.value) }}</text>
        </g>
        <!-- X 轴标签 -->
        <g class="sql-chart__xaxis">
          <text
            v-for="(tick, i) in xTicks"
            :key="`xl${i}`"
            :x="tick.x" :y="height - padBottom + 16"
            text-anchor="middle"
          >{{ tick.label }}</text>
        </g>
        <!-- 系列线条 -->
        <g v-for="(s, i) in series" :key="s.name">
          <path
            :d="s.path"
            :stroke="palette[i % palette.length]"
            stroke-width="1.5"
            fill="none"
          />
        </g>
      </svg>
    </div>
    <div class="sql-chart__legend">
      <span
        v-for="(s, i) in series"
        :key="`lg${i}`"
        class="sql-chart__legend-item"
      >
        <span class="sql-chart__swatch" :style="{ background: palette[i % palette.length] }" />
        {{ s.name }}
      </span>
    </div>
  </div>
  <n-text v-else depth="3" style="font-size: 12px">
    {{ chartHint }}
  </n-text>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { NSelect, NSpace, NText, type SelectOption } from 'naive-ui';

interface Props {
  columns: string[];
  rows: Record<string, unknown>[];
}
const props = defineProps<Props>();

const palette = [
  '#2c7be5', '#0d3b66', '#e85d75', '#52b788',
  '#f4a261', '#7b2cbf', '#118ab2', '#ef476f',
];

const width = 720;
const height = 260;
const padLeft = 56;
const padRight = 16;
const padTop = 12;
const padBottom = 28;

function isFiniteNumber(v: unknown): v is number {
  return typeof v === 'number' && Number.isFinite(v);
}

function tryParseTime(v: unknown): number | null {
  if (typeof v === 'number' && Number.isFinite(v)) return v;
  if (typeof v === 'string') {
    const t = Date.parse(v);
    if (!Number.isNaN(t)) return t;
    const n = Number(v);
    if (Number.isFinite(n)) return n;
  }
  return null;
}

const numericColumns = computed(() =>
  props.columns.filter((c) =>
    props.rows.some((r) => isFiniteNumber(r[c]))));

const timeCandidates = computed(() => {
  const lc = props.columns.filter((c) => /^(time|ts|timestamp)$/i.test(c));
  if (lc.length > 0) return lc;
  // 找出 >50% 行可解析为时间的列
  return props.columns.filter((c) => {
    let ok = 0;
    for (const r of props.rows) if (tryParseTime(r[c]) !== null) ok += 1;
    return ok > props.rows.length / 2;
  });
});

const timeColumn = ref<string>('');
const valueColumns = ref<string[]>([]);
const groupByTag = ref<string | null>(null);

watch(() => [props.columns, props.rows], () => {
  timeColumn.value = timeCandidates.value[0] ?? props.columns[0] ?? '';
  // 默认选中最多 3 个数值列
  valueColumns.value = numericColumns.value
    .filter((c) => c !== timeColumn.value)
    .slice(0, 3);
  groupByTag.value = null;
}, { immediate: true });

const timeOptions = computed<SelectOption[]>(() =>
  props.columns.map((c) => ({ label: c, value: c })));
const valueOptions = computed<SelectOption[]>(() =>
  numericColumns.value
    .filter((c) => c !== timeColumn.value)
    .map((c) => ({ label: c, value: c })));
const tagOptions = computed<SelectOption[]>(() =>
  props.columns
    .filter((c) => c !== timeColumn.value && !valueColumns.value.includes(c))
    .filter((c) => {
      // 只把字符串 / 低基数列当作 tag
      let strCount = 0;
      let unique = new Set<string>();
      for (const r of props.rows) {
        const v = r[c];
        if (typeof v === 'string') {
          strCount += 1;
          unique.add(v);
        }
      }
      return strCount > props.rows.length / 2 && unique.size <= 12;
    })
    .map((c) => ({ label: c, value: c })));

interface Series {
  name: string;
  points: Array<{ x: number; y: number }>;
  path: string;
}

const chartReady = computed(() =>
  props.rows.length > 0
  && !!timeColumn.value
  && valueColumns.value.length > 0);

const chartHint = computed(() => {
  if (props.rows.length === 0) return '没有可绘制的数据。';
  if (numericColumns.value.length === 0) return '没有数值列，无法绘制图表。';
  if (!timeColumn.value) return '请选择时间列。';
  return '请至少选择一个数值字段。';
});

const series = computed<Series[]>(() => {
  if (!chartReady.value) return [];
  const buckets = new Map<string, Array<{ x: number; y: number }>>();

  for (const row of props.rows) {
    const x = tryParseTime(row[timeColumn.value]);
    if (x === null) continue;
    const tag = groupByTag.value
      ? String(row[groupByTag.value] ?? 'null')
      : null;
    for (const col of valueColumns.value) {
      const y = row[col];
      if (!isFiniteNumber(y)) continue;
      const key = tag ? `${col} · ${tag}` : col;
      let arr = buckets.get(key);
      if (!arr) { arr = []; buckets.set(key, arr); }
      arr.push({ x, y });
    }
  }

  const all: Series[] = [];
  for (const [name, points] of buckets) {
    points.sort((a, b) => a.x - b.x);
    all.push({ name, points, path: '' });
  }
  if (all.length === 0) return [];

  // 计算坐标域
  let xMin = Infinity, xMax = -Infinity, yMin = Infinity, yMax = -Infinity;
  for (const s of all) {
    for (const p of s.points) {
      if (p.x < xMin) xMin = p.x;
      if (p.x > xMax) xMax = p.x;
      if (p.y < yMin) yMin = p.y;
      if (p.y > yMax) yMax = p.y;
    }
  }
  if (xMin === xMax) { xMax = xMin + 1; }
  if (yMin === yMax) { yMax = yMin + 1; }

  const w = width - padLeft - padRight;
  const h = height - padTop - padBottom;
  for (const s of all) {
    const segs: string[] = [];
    s.points.forEach((p, idx) => {
      const x = padLeft + ((p.x - xMin) / (xMax - xMin)) * w;
      const y = padTop + h - ((p.y - yMin) / (yMax - yMin)) * h;
      segs.push(`${idx === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`);
    });
    s.path = segs.join(' ');
  }
  return all;
});

const yTicks = computed(() => {
  if (series.value.length === 0) return [];
  let yMin = Infinity, yMax = -Infinity;
  for (const s of series.value) {
    for (const p of s.points) {
      if (p.y < yMin) yMin = p.y;
      if (p.y > yMax) yMax = p.y;
    }
  }
  if (yMin === yMax) { yMin -= 1; yMax += 1; }
  const h = height - padTop - padBottom;
  const out: Array<{ y: number; value: number }> = [];
  for (let i = 0; i <= 4; i += 1) {
    const t = i / 4;
    const value = yMin + (yMax - yMin) * (1 - t);
    out.push({ y: padTop + h * t, value });
  }
  return out;
});

const xTicks = computed(() => {
  if (series.value.length === 0) return [];
  let xMin = Infinity, xMax = -Infinity;
  for (const s of series.value) {
    for (const p of s.points) {
      if (p.x < xMin) xMin = p.x;
      if (p.x > xMax) xMax = p.x;
    }
  }
  if (xMin === xMax) { xMax = xMin + 1; }
  const w = width - padLeft - padRight;
  const out: Array<{ x: number; label: string }> = [];
  for (let i = 0; i <= 4; i += 1) {
    const t = i / 4;
    const value = xMin + (xMax - xMin) * t;
    out.push({ x: padLeft + w * t, label: formatX(value) });
  }
  return out;
});

function formatX(value: number): string {
  // 当数值看起来像 epoch 毫秒（> 1990-01-01）时按时间显示，否则原样数字
  if (value > 631_152_000_000) {
    const d = new Date(value);
    const hh = String(d.getHours()).padStart(2, '0');
    const mm = String(d.getMinutes()).padStart(2, '0');
    const ss = String(d.getSeconds()).padStart(2, '0');
    return `${hh}:${mm}:${ss}`;
  }
  return formatTick(value);
}

function formatTick(value: number): string {
  if (Math.abs(value) >= 1000) return value.toFixed(0);
  if (Math.abs(value) >= 1) return value.toFixed(2);
  return value.toPrecision(2);
}
</script>

<style scoped>
.sql-chart { display: flex; flex-direction: column; gap: 6px; }
.sql-chart__head { font-size: 12px; }
.sql-chart__label { color: #678; font-size: 12px; }
.sql-chart__canvas {
  width: 100%;
  background: #fafcfe;
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: 6px;
  overflow: hidden;
}
.sql-chart__canvas svg { width: 100%; height: auto; display: block; }
.sql-chart__grid line { stroke: rgba(0, 0, 0, 0.08); stroke-dasharray: 3 3; }
.sql-chart__grid text { font-size: 10px; fill: #678; }
.sql-chart__xaxis text { font-size: 10px; fill: #678; }
.sql-chart__legend {
  display: flex; flex-wrap: wrap; gap: 12px;
  font-size: 12px; color: #345;
}
.sql-chart__legend-item { display: inline-flex; align-items: center; gap: 4px; }
.sql-chart__swatch {
  display: inline-block;
  width: 10px; height: 10px;
  border-radius: 2px;
}
</style>
