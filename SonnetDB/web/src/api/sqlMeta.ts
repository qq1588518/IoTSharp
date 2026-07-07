import type { SqlResultSet } from './sql';

/**
 * SQL Console 元命令解析结果。
 * <p>
 * SonnetDB 服务端按 URL 路径 (`/v1/db/{db}/sql`) 强绑定目标库，没有连接级
 * "current database" 状态。为了让用户在 SQL Console 里有等价于 MySQL `USE`
 * 的体验，下列命令在客户端就被拦截：
 * <ul>
 *   <li><c>USE &lt;db&gt;</c> / <c>USE system</c> — 切换 SQL Console 的「目标」选择器；</li>
 *   <li><c>SELECT current_database()</c> / <c>SELECT database()</c>
 *       / <c>SHOW CURRENT_DATABASE</c> / <c>SHOW CURRENT DATABASE</c>
 *       — 直接以一行结果集返回当前目标库。</li>
 * </ul>
 * 这些命令不会被发往服务端，因此也不会触发服务端 SQL parser 的「未知关键字」错误。
 */
export type SqlMetaCommand =
  | { kind: 'use'; database: string }
  | { kind: 'current-database' };

/** 把单条语句识别为 console 元命令；不匹配则返回 null。 */
export function parseSqlMetaCommand(sql: string): SqlMetaCommand | null {
  const trimmed = sql.trim().replace(/;+\s*$/u, '');
  if (trimmed.length === 0) return null;

  // USE <name>
  const useMatch = /^use\s+(`?)([A-Za-z_][A-Za-z0-9_]*|\*)\1$/i.exec(trimmed);
  if (useMatch) {
    return { kind: 'use', database: useMatch[2] };
  }

  // SHOW CURRENT_DATABASE | SHOW CURRENT DATABASE
  if (/^show\s+current[\s_]+database$/i.test(trimmed)) {
    return { kind: 'current-database' };
  }

  // SELECT current_database() | SELECT database()
  if (/^select\s+(current_database|database)\s*\(\s*\)$/i.test(trimmed)) {
    return { kind: 'current-database' };
  }

  return null;
}

/** 构造一个客户端合成的「成功」结果集，用 markdown 文本展示给用户。 */
export function buildClientResultSet(
  columns: string[],
  rows: unknown[][],
  elapsedMs = 0,
): SqlResultSet {
  return {
    columns,
    rows,
    end: {
      type: 'end',
      rowCount: rows.length,
      recordsAffected: 0,
      elapsedMs,
    },
    error: null,
    hasColumns: columns.length > 0,
  };
}

/** 构造一个客户端合成的错误结果集。 */
export function buildClientErrorResultSet(code: string, message: string): SqlResultSet {
  return {
    columns: [],
    rows: [],
    end: null,
    error: { type: 'error', code, message },
    hasColumns: false,
  };
}
