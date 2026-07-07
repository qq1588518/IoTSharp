<template>
  <n-card title="授权" :bordered="false">
    <n-space vertical :size="12">
      <n-space>
        <n-select v-model:value="form.user" :options="userOptions" placeholder="用户" style="width:160px;" />
        <n-select v-model:value="form.db" :options="dbOptions" placeholder="数据库" style="width:160px;" />
        <n-select v-model:value="form.permission" :options="permissionOptions" placeholder="权限" style="width:140px;" />
        <n-button type="primary" @click="onGrant">GRANT</n-button>
        <n-button @click="reload">刷新</n-button>
      </n-space>
      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />
      <n-data-table :columns="cols" :data="grants" :bordered="false" size="small" />
    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { computed, h, onMounted, reactive, ref } from 'vue';
import {
  NCard, NSpace, NSelect, NButton, NAlert, NDataTable, NPopconfirm,
  useMessage, type DataTableColumns, type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { execControlPlaneSql, rowsToObjects } from '@/api/sql';
import { listDatabases } from '@/api/server';

interface GrantRow { user_name: string; database: string; permission: string; [k: string]: unknown }

const auth = useAuthStore();
const message = useMessage();

const grants = ref<GrantRow[]>([]);
const databases = ref<string[]>([]);
const users = ref<string[]>([]);
const errorMsg = ref('');

const form = reactive<{ user: string | null; db: string | null; permission: 'READ' | 'WRITE' | 'ADMIN' | null }>({
  user: null, db: null, permission: 'READ',
});

const userOptions = computed<SelectOption[]>(() => users.value.map((u) => ({ label: u, value: u })));
const dbOptions = computed<SelectOption[]>(() => databases.value.map((d) => ({ label: d, value: d })));
const permissionOptions: SelectOption[] = [
  { label: 'READ', value: 'READ' },
  { label: 'WRITE', value: 'WRITE' },
  { label: 'ADMIN', value: 'ADMIN' },
];

const cols: DataTableColumns<GrantRow> = [
  { title: '用户', key: 'user_name' },
  { title: '数据库', key: 'database' },
  { title: '权限', key: 'permission' },
  {
    title: '操作', key: 'actions', width: 100,
    render: (r) => h(NPopconfirm, {
      onPositiveClick: () => onRevoke(r.user_name, r.database),
    }, {
      trigger: () => h(NButton, { size: 'small', type: 'error', text: true }, { default: () => 'REVOKE' }),
      default: () => `确认 REVOKE ${r.user_name}@${r.database}？`,
    }),
  },
];

async function reload(): Promise<void> {
  errorMsg.value = '';
  const [grRs, usrRs, dbResult] = await Promise.all([
    execControlPlaneSql(auth.api, 'SHOW GRANTS'),
    execControlPlaneSql(auth.api, 'SHOW USERS'),
    listDatabases(auth.api),
  ]);
  if (grRs.error) { errorMsg.value = grRs.error.message; return; }
  grants.value = rowsToObjects<GrantRow>(grRs);
  if (!usrRs.error) users.value = usrRs.rows.map((r) => String(r[0]));
  if (!dbResult.error) databases.value = dbResult.databases;
}

async function onGrant(): Promise<void> {
  if (!form.user || !form.db || !form.permission) { message.error('请填写用户、数据库与权限。'); return; }
  const escapedDb = form.db.replace(/"/g, '""');
  const escapedUser = form.user.replace(/"/g, '""');
  const sql = `GRANT ${form.permission} ON DATABASE "${escapedDb}" TO "${escapedUser}"`;
  const rs = await execControlPlaneSql(auth.api, sql);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已授权 ${form.user}@${form.db} ${form.permission}`);
  await reload();
}

async function onRevoke(user: string, db: string): Promise<void> {
  const escapedDb = db.replace(/"/g, '""');
  const escapedUser = user.replace(/"/g, '""');
  const rs = await execControlPlaneSql(auth.api, `REVOKE ON DATABASE "${escapedDb}" FROM "${escapedUser}"`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已撤销 ${user}@${db}`);
  await reload();
}

onMounted(reload);
</script>
