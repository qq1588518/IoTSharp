import { defineStore } from 'pinia';
import { computed, ref, watch } from 'vue';
import type { CopilotMessage } from '@/api/copilot';
import { CONTROL_PLANE_KEY } from '@/stores/sqlConsole';

/**
 * 单条 Copilot 会话。`messages` 仅保留 user/assistant 文本回合（不含 tool 中间事件）。
 */
export interface CopilotSession {
  id: string;
  title: string;
  db: string;
  messages: CopilotMessage[];
  createdAt: number;
  updatedAt: number;
}

const STORAGE_KEY = 'sndb.copilot.sessions.v1';
const MAX_SESSIONS = 50;
const TITLE_MAX_LEN = 32;

interface PersistedShape {
  v: 1;
  currentId: string | null;
  sessions: CopilotSession[];
}

function load(): PersistedShape {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { v: 1, currentId: null, sessions: [] };
    const parsed = JSON.parse(raw) as PersistedShape;
    if (parsed?.v !== 1 || !Array.isArray(parsed.sessions)) {
      return { v: 1, currentId: null, sessions: [] };
    }
    return parsed;
  } catch {
    return { v: 1, currentId: null, sessions: [] };
  }
}

function persist(state: PersistedShape): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // 配额满 / 隐私模式：忽略
  }
}

function genId(): string {
  return `s_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;
}

function deriveTitle(messages: CopilotMessage[], fallback: string): string {
  const firstUser = messages.find((m) => m.role === 'user' && m.content.trim().length > 0);
  const text = firstUser?.content.trim() ?? fallback;
  return text.length > TITLE_MAX_LEN ? `${text.slice(0, TITLE_MAX_LEN)}…` : text;
}

export const useCopilotSessionsStore = defineStore('copilotSessions', () => {
  const initial = load();
  const sessions = ref<CopilotSession[]>(initial.sessions);
  const currentId = ref<string | null>(initial.currentId);

  const current = computed<CopilotSession | null>(() =>
    sessions.value.find((s) => s.id === currentId.value) ?? null,
  );

  /** 按 updatedAt 倒序的会话列表（用于侧栏渲染）。 */
  const recent = computed<CopilotSession[]>(() =>
    [...sessions.value].sort((a, b) => b.updatedAt - a.updatedAt),
  );

  function create(db: string): CopilotSession {
    const now = Date.now();
    const session: CopilotSession = {
      id: genId(),
      title: '新会话',
      db,
      messages: [],
      createdAt: now,
      updatedAt: now,
    };
    sessions.value.unshift(session);
    trim();
    currentId.value = session.id;
    return session;
  }

  function switchTo(id: string): void {
    if (sessions.value.some((s) => s.id === id)) {
      currentId.value = id;
    }
  }

  function rename(id: string, title: string): void {
    const s = sessions.value.find((x) => x.id === id);
    if (!s) return;
    const trimmed = title.trim();
    if (trimmed.length === 0) return;
    s.title = trimmed.length > TITLE_MAX_LEN ? `${trimmed.slice(0, TITLE_MAX_LEN)}…` : trimmed;
    s.updatedAt = Date.now();
  }

  function remove(id: string): void {
    const idx = sessions.value.findIndex((s) => s.id === id);
    if (idx < 0) return;
    sessions.value.splice(idx, 1);
    if (currentId.value === id) {
      currentId.value = sessions.value[0]?.id ?? null;
    }
  }

  function clearAll(): void {
    sessions.value = [];
    currentId.value = null;
  }

  function hideControlPlaneForRegularUser(): void {
    for (const session of sessions.value) {
      if (session.db === CONTROL_PLANE_KEY) {
        session.db = '';
      }
    }
  }

  /**
   * 把一轮对话（user + assistant 各一条）追加到指定会话；首次写入时自动从首条 user 消息派生标题。
   * 如果 id 不存在，则新建一个会话。
   */
  function appendTurn(id: string | null, db: string, user: CopilotMessage, assistant: CopilotMessage): CopilotSession {
    let session = id ? sessions.value.find((s) => s.id === id) ?? null : null;
    if (!session) {
      session = create(db);
    }
    session.messages.push(user, assistant);
    session.updatedAt = Date.now();
    if (session.title === '新会话') {
      session.title = deriveTitle(session.messages, '新会话');
    }
    if (db && !session.db) session.db = db;
    return session;
  }

  function appendMessage(id: string | null, db: string, message: CopilotMessage): CopilotSession {
    let session = id ? sessions.value.find((s) => s.id === id) ?? null : null;
    if (!session) {
      session = create(db);
    }
    session.messages.push(message);
    session.updatedAt = Date.now();
    if (session.title === '新会话') {
      session.title = deriveTitle(session.messages, '新会话');
    }
    if (db && !session.db) session.db = db;
    return session;
  }

  function setMessages(id: string, messages: CopilotMessage[]): void {
    const s = sessions.value.find((x) => x.id === id);
    if (!s) return;
    s.messages = messages;
    s.updatedAt = Date.now();
    if (s.title === '新会话') {
      s.title = deriveTitle(messages, '新会话');
    }
  }

  function trim(): void {
    if (sessions.value.length <= MAX_SESSIONS) return;
    sessions.value.sort((a, b) => b.updatedAt - a.updatedAt);
    sessions.value = sessions.value.slice(0, MAX_SESSIONS);
  }

  // 任何 sessions / currentId 变化都同步到 localStorage（深度监听）。
  watch(
    [sessions, currentId],
    () => {
      persist({ v: 1, currentId: currentId.value, sessions: sessions.value });
    },
    { deep: true },
  );

  return {
    sessions,
    currentId,
    current,
    recent,
    create,
    switchTo,
    rename,
    remove,
    clearAll,
    hideControlPlaneForRegularUser,
    appendTurn,
    appendMessage,
    setMessages,
  };
});
