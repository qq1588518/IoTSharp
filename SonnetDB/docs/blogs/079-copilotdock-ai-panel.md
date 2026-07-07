## CopilotDock 全局浮动面板：拖拽、全屏、页面感知与 50 条历史会话

CopilotDock 是 SonnetDB Web 管理界面中的 AI 助手面板，以浮动气泡形式存在于页面右下角。它不是一个简单的聊天窗口，而是一个深度感知页面上下文、具备权限管理能力、支持 50 条会话历史的智能工作台。

### 全局浮动设计

面板有两种形态：

- **折叠态**：右下角的 "AI" 圆形气泡按钮，不占用页面空间
- **展开态**：可拖拽、可全屏的浮动面板

```vue
<div v-if="!visible" class="copilot-fab" @click="open" title="打开 Copilot 助手">
  <span class="copilot-fab-icon">AI</span>
</div>
<div v-else ref="dockEl" class="copilot-dock"
     :class="{ 'is-fullscreen': fullscreen }" :style="dockStyle">
```

用户可以通过鼠标拖拽面板标题栏改变位置，点击全屏按钮展开到满屏，也可以随时折叠回收。

### 页面感知快捷能力

面板会自动感知当前访问的页面，在输入框上方显示上下文标签和快捷操作按钮。例如在 SQL Console 页面时，Copilot 会显示"SQL Console"标签，并建议"解释当前 SQL"或"优化查询"等快捷操作。这通过计算属性 `pageContext` 和 `assistantActions` 实现：

```typescript
const pageContext = computed(() => { /* 自动识别当前路由 */ });
const assistantActions = computed(() => { /* 根据页面生成快捷操作按钮 */ });
```

用户还可以手动关闭或重新启用页面上下文，灵活控制 Copilot 的感知范围。

### 只读 / 读写模式

M7 特性引入了明确的权限模式切换（M7）：

- **只读模式**（默认）：绿色标签，Copilot 只能执行查询类 SQL，杜绝误写入
- **读写模式**：黄色警告标签，可执行写入语句，但仍受凭据本身权限上限约束

切换时有内联确认条，防止误操作：

```html
<n-tag v-if="permissionMode === 'read-only'" @click="permConfirmVisible = !permConfirmVisible"
       size="tiny" type="success" :bordered="false">只读模式</n-tag>
<transition name="perm-confirm">
  <div v-if="permConfirmVisible" class="copilot-dock__perm-confirm">
    <n-text>切换后 Copilot 可执行写入语句，是否启用？</n-text>
    <n-button @click="permissionMode = 'read-write'">启用读写</n-button>
    <n-button @click="permConfirmVisible = false">取消</n-button>
  </div>
</transition>
```

### 50 条会话历史

`useCopilotSessionsStore` Pinia store 管理会话的完整生命周期：

- 自动持久化到 `localStorage`（Key: `sndb.copilot.sessions.v1`）
- 最多保留 50 条最近会话，超出自动裁剪
- 每条会话记录：标题（从首条消息自动派生）、数据库名、消息轮数、时间戳
- 支持重命名、删除、清空全部

```typescript
const STORAGE_KEY = 'sndb.copilot.sessions.v1';
const MAX_SESSIONS = 50;
function appendTurn(id, db, user, assistant): CopilotSession {
    let session = id ? sessions.value.find(...) : null;
    if (!session) session = create(db);
    session.messages.push(user, assistant);
    session.updatedAt = Date.now();
    // 首次自动派生标题
    if (session.title === '新会话')
        session.title = deriveTitle(session.messages, '新会话');
    return session;
}
```

### 流式渲染与引用

CopilotDock 通过 SSE 流逐行渲染 AI 回答，同时显示引用来源（文档、技能、工具结果）。每条引用包含编号、标题、来源和摘要片段，用户点击即可溯源，保证 AI 回答的可验证性。
