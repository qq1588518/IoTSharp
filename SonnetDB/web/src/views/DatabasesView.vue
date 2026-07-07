<template>
  <n-card title="数据库" :bordered="false">
    <n-space vertical :size="12">
      <n-space>
        <n-input v-model:value="newName" placeholder="新数据库名（字母数字下划线短横线）" style="width:280px;" />
        <n-button type="primary" :disabled="!auth.isSuperuser" @click="onCreate">CREATE DATABASE</n-button>
        <n-button @click="reload">刷新</n-button>
      </n-space>
      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />
      <n-data-table :columns="cols" :data="rows" :bordered="false" size="small" />
    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue';
import {
  NCard, NSpace, NInput, NButton, NAlert, NDataTable, NPopconfirm, NTag, useMessage,
  type DataTableColumns,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { useEventsStore } from '@/stores/events';
import { execControlPlaneSql, isValidIdentifier } from '@/api/sql';
import { listDatabases, loadSegmentCounts } from '@/api/server';

const auth = useAuthStore();
const events = useEventsStore();
const message = useMessage();

interface DbRow { name: string; segment_count: number; status: string }
const databases = ref<string[]>([]);
const segmentCounts = ref<Record<string, number>>({});
const newName = ref('');
const errorMsg = ref('');

const rows = computed<DbRow[]>(() => databases.value.map((name) => ({
  name,
  segment_count: segmentCounts.value[name] ?? 0,
  status: 'online',
})));

const cols = computed<DataTableColumns<DbRow>>(() => [
  { title: '名称', key: 'name' },
  {
    title: '状态',
    key: 'status',
    width: 100,
    render: () => h(NTag, { type: 'success', size: 'small' }, { default: () => '在线' }),
  },
  { title: 'Segment', key: 'segment_count', width: 100 },
  {
    title: '操作',
    key: 'actions',
    width: 120,
    render: (row) => h(NPopconfirm, {
      onPositiveClick: () => onDrop(row.name),
      disabled: !auth.isSuperuser,
    }, {
      trigger: () => h(NButton, {
        size: 'small', type: 'error', text: true, disabled: !auth.isSuperuser,
      }, { default: () => 'DROP' }),
      default: () => `确认 DROP DATABASE ${row.name}？数据将不可恢复。`,
    }),
  },
]);

async function reload(): Promise<void> {
  errorMsg.value = '';
  const [dbResult, segmentsResult] = await Promise.all([
    listDatabases(auth.api),
    loadSegmentCounts(auth.api),
  ]);
  if (dbResult.error) { errorMsg.value = dbResult.error.message; return; }
  databases.value = dbResult.databases;
  if (!segmentsResult.error) {
    segmentCounts.value = segmentsResult.counts;
  }
}

async function onCreate(): Promise<void> {
  const name = newName.value.trim();
  if (!isValidIdentifier(name)) {
    message.error('名称必须以字母开头，仅包含字母数字下划线。');
    return;
  }
  const rs = await execControlPlaneSql(auth.api, `CREATE DATABASE ${name}`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已创建 ${name}`);
  newName.value = '';
  await reload();
}

async function onDrop(name: string): Promise<void> {
  const rs = await execControlPlaneSql(auth.api, `DROP DATABASE ${name}`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已删除 ${name}`);
  await reload();
}

onMounted(reload);

// SSE: db 事件触发刷新；metrics 帧覆盖 segment 数
watch(() => events.dbEventBumper, () => { void reload(); });
watch(() => events.metrics, (m) => {
  if (m?.perDatabaseSegments) segmentCounts.value = { ...m.perDatabaseSegments };
});
</script>
