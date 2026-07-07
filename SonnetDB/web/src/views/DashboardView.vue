<template>
  <div>
    <n-grid :cols="3" :x-gap="16" :y-gap="16">
      <n-gi>
        <n-card title="数据库数量">
          <n-statistic :value="databases.length" />
        </n-card>
      </n-gi>
      <n-gi v-if="auth.isSuperuser">
        <n-card title="用户数量">
          <n-statistic :value="users.length" />
        </n-card>
      </n-gi>
      <n-gi v-if="auth.isSuperuser">
        <n-card title="授权条目">
          <n-statistic :value="grants.length" />
        </n-card>
      </n-gi>
    </n-grid>

    <n-card title="数据库" style="margin-top:16px;">
      <n-data-table :columns="dbCols" :data="dbRows" :bordered="false" size="small" />
    </n-card>

    <n-card v-if="auth.isSuperuser" title="用户" style="margin-top:16px;">
      <n-data-table :columns="userCols" :data="users" :bordered="false" size="small" />
    </n-card>

    <n-alert v-if="lastError" type="error" :title="lastError" style="margin-top:16px;" closable />
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue';
import {
  NCard, NGrid, NGi, NStatistic, NDataTable, NAlert, NTag, type DataTableColumns,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { useEventsStore } from '@/stores/events';
import { execControlPlaneSql, rowsToObjects } from '@/api/sql';
import { listDatabases, loadSegmentCounts } from '@/api/server';

interface UserRow { name: string; is_superuser: boolean; created_utc: string; token_count: number; [k: string]: unknown }
interface GrantRow { user_name: string; database: string; permission: string; [k: string]: unknown }
interface DbRow { name: string; segment_count: number; status: string }

const auth = useAuthStore();
const events = useEventsStore();

const databases = ref<string[]>([]);
const users = ref<UserRow[]>([]);
const grants = ref<GrantRow[]>([]);
const segmentCounts = ref<Record<string, number>>({});
const lastError = ref<string>('');

const dbRows = computed<DbRow[]>(() => databases.value.map((name) => ({
  name,
  segment_count: segmentCounts.value[name] ?? 0,
  status: 'online',
})));

const dbCols: DataTableColumns<DbRow> = [
  { title: '数据库', key: 'name' },
  {
    title: '状态',
    key: 'status',
    render: () => h(NTag, { type: 'success', size: 'small' }, { default: () => '在线' }),
  },
  { title: 'Segment', key: 'segment_count' },
];
const userCols: DataTableColumns<UserRow> = [
  { title: '用户名', key: 'name' },
  { title: '超级用户', key: 'is_superuser', render: (r) => h('span', r.is_superuser ? '是' : '否') },
  { title: '创建时间', key: 'created_utc' },
  { title: 'Token 数', key: 'token_count' },
];

async function loadAll(): Promise<void> {
  lastError.value = '';
  const [dbResult, segmentsResult] = await Promise.all([
    listDatabases(auth.api),
    loadSegmentCounts(auth.api),
  ]);

  if (dbResult.error) {
    lastError.value = dbResult.error.message;
    return;
  }

  databases.value = dbResult.databases;
  if (!segmentsResult.error) {
    segmentCounts.value = segmentsResult.counts;
  }

  if (auth.isSuperuser) {
    const usrRs = await execControlPlaneSql(auth.api, 'SHOW USERS');
    if (!usrRs.error) users.value = rowsToObjects<UserRow>(usrRs);

    const grRs = await execControlPlaneSql(auth.api, 'SHOW GRANTS');
    if (!grRs.error) grants.value = rowsToObjects<GrantRow>(grRs);
  }
}

onMounted(loadAll);

// SSE: 数据库 CREATE/DROP 事件触发后台静默重载
watch(() => events.dbEventBumper, () => { void loadAll(); });
// SSE: 周期性 metrics 帧到达时直接覆盖各 db 的 segment 数（避免每 5 秒扫一次 /metrics 文本）
watch(() => events.metrics, (m) => {
  if (m?.perDatabaseSegments) segmentCounts.value = { ...m.perDatabaseSegments };
});
</script>
