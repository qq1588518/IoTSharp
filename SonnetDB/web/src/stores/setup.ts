import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import { createApiClient } from '@/api/client';
import {
  fetchSetupStatus,
  initializeSetup,
  type SetupInitializePayload,
  type SetupInitializeResponse,
  type SetupStatus,
} from '@/api/setup';

export const useSetupStore = defineStore('setup', () => {
  const api = createApiClient(() => null);
  const status = ref<SetupStatus | null>(null);
  const loading = ref(false);
  const loaded = ref(false);

  const needsSetup = computed(() => status.value?.needsSetup ?? true);
  const serverId = computed(() => status.value?.serverId ?? null);
  const organization = computed(() => status.value?.organization ?? null);
  const suggestedServerId = computed(() => status.value?.suggestedServerId ?? 'sndb-local');

  async function refresh(): Promise<SetupStatus> {
    loading.value = true;
    try {
      const next = await fetchSetupStatus(api);
      status.value = next;
      loaded.value = true;
      return next;
    } finally {
      loading.value = false;
    }
  }

  async function ensureLoaded(): Promise<SetupStatus> {
    if (status.value && loaded.value) {
      return status.value;
    }
    return refresh();
  }

  async function bootstrap(payload: SetupInitializePayload): Promise<SetupInitializeResponse> {
    const response = await initializeSetup(api, payload);
    status.value = {
      needsSetup: false,
      suggestedServerId: response.serverId,
      serverId: response.serverId,
      organization: response.organization,
      userCount: 1,
      databaseCount: status.value?.databaseCount ?? 0,
    };
    loaded.value = true;
    return response;
  }

  return {
    status,
    loading,
    loaded,
    needsSetup,
    serverId,
    organization,
    suggestedServerId,
    refresh,
    ensureLoaded,
    bootstrap,
  };
});
