<template>
  <n-card title="用户管理" :bordered="false">
    <n-space vertical :size="12">
      <n-space>
        <n-input v-model:value="form.name" placeholder="用户名" style="width:160px;" />
        <n-input v-model:value="form.password" type="password" placeholder="密码" style="width:200px;" show-password-on="click" />
        <n-checkbox v-model:checked="form.superuser">SUPERUSER</n-checkbox>
        <n-button type="primary" @click="onCreate">CREATE USER</n-button>
        <n-button @click="reload">刷新</n-button>
      </n-space>
      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />
      <n-data-table :columns="cols" :data="users" :bordered="false" size="small" />
    </n-space>

    <n-modal v-model:show="pwdModal" preset="card" title="修改密码" style="width:400px;">
      <n-space vertical>
        <n-text>用户：{{ pwdTarget }}</n-text>
        <n-input v-model:value="pwdNew" type="password" placeholder="新密码" show-password-on="click" />
        <n-space justify="end">
          <n-button @click="pwdModal = false">取消</n-button>
          <n-button type="primary" @click="onAlterPwd">提交</n-button>
        </n-space>
      </n-space>
    </n-modal>
  </n-card>
</template>

<script setup lang="ts">
import { h, onMounted, reactive, ref } from 'vue';
import {
  NCard, NSpace, NInput, NButton, NAlert, NDataTable, NCheckbox, NPopconfirm,
  NModal, NText, useMessage,
  type DataTableColumns,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { execControlPlaneSql, rowsToObjects, quote, isValidIdentifier } from '@/api/sql';

interface UserRow { name: string; is_superuser: boolean; created_utc: string; token_count: number; [k: string]: unknown }

const auth = useAuthStore();
const message = useMessage();

const users = ref<UserRow[]>([]);
const errorMsg = ref('');

const form = reactive({ name: '', password: '', superuser: false });

const pwdModal = ref(false);
const pwdTarget = ref('');
const pwdNew = ref('');

const cols: DataTableColumns<UserRow> = [
  { title: '用户名', key: 'name' },
  { title: '超级用户', key: 'is_superuser', render: (r) => h('span', r.is_superuser ? '是' : '否') },
  { title: '创建时间', key: 'created_utc' },
  { title: 'Token', key: 'token_count' },
  {
    title: '操作', key: 'actions', width: 200,
    render: (r) => h(NSpace, { size: 8 }, {
      default: () => [
        h(NButton, { size: 'small', onClick: () => openPwd(r.name) }, { default: () => '改密' }),
        h(NPopconfirm, {
          onPositiveClick: () => onDrop(r.name),
        }, {
          trigger: () => h(NButton, { size: 'small', type: 'error', text: true }, { default: () => 'DROP' }),
          default: () => `确认 DROP USER ${r.name}？`,
        }),
      ],
    }),
  },
];

async function reload(): Promise<void> {
  errorMsg.value = '';
  const rs = await execControlPlaneSql(auth.api, 'SHOW USERS');
  if (rs.error) { errorMsg.value = rs.error.message; return; }
  users.value = rowsToObjects<UserRow>(rs);
}

async function onCreate(): Promise<void> {
  if (!isValidIdentifier(form.name)) { message.error('用户名必须以字母开头，仅含字母数字下划线。'); return; }
  if (form.password.length < 1) { message.error('请填写密码。'); return; }
  const sup = form.superuser ? ' SUPERUSER' : '';
  const sql = `CREATE USER ${form.name} WITH PASSWORD ${quote(form.password)}${sup}`;
  const rs = await execControlPlaneSql(auth.api, sql);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已创建用户 ${form.name}`);
  form.name = ''; form.password = ''; form.superuser = false;
  await reload();
}

function openPwd(name: string): void {
  pwdTarget.value = name; pwdNew.value = ''; pwdModal.value = true;
}

async function onAlterPwd(): Promise<void> {
  if (!pwdNew.value) { message.error('请输入新密码。'); return; }
  const sql = `ALTER USER ${quote(pwdTarget.value)} WITH PASSWORD ${quote(pwdNew.value)}`;
  const rs = await execControlPlaneSql(auth.api, sql);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已更新 ${pwdTarget.value} 的密码`);
  pwdModal.value = false;
}

async function onDrop(name: string): Promise<void> {
  const rs = await execControlPlaneSql(auth.api, `DROP USER ${quote(name)}`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已删除 ${name}`);
  await reload();
}

onMounted(reload);
</script>
