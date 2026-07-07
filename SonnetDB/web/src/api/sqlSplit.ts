/**
 * 把客户端粘贴的多条 SQL 语句按顶层分号 `;` 切分成单语句数组。
 *
 * 服务端 `SqlParser` 一次只接受一条语句（结尾的分号可选），
 * 因此 SQL Console 在发请求前先做切分。切分逻辑必须忽略：
 * - 单引号 `'...'` 字符串（包含两个连续单引号 `''` 的转义）
 * - 行注释 `-- ...` / `// ...`
 * - `REM ...`（仅在行首或分号后视为注释，避免吞掉合法标识符）
 * - 块注释 `/* ... *\/`
 *
 * 标识符用反引号或双引号在 SonnetDB 暂不支持，这里也顺带忽略。
 *
 * 返回值：每个元素是去掉首尾空白、且含有实质 SQL 内容（非纯注释）的单条语句（不带尾随分号）。
 */

/**
 * 判断字符串在去掉注释后是否还有非空字符。
 * 仅用于过滤纯注释片段，不用于完整词法分析。
 */
function hasSubstantiveContent(s: string): boolean {
  // 去掉块注释
  let t = s.replace(/\/\*[\s\S]*?\*\//g, '');
  // 去掉行注释
  t = t.replace(/--[^\n]*/g, '');
  t = t.replace(/\/\/[^\n]*/g, '');
  t = t.replace(/^[ \t]*rem(?=\s|$)[^\r\n]*/gim, '');
  return t.trim().length > 0;
}

function startsWithIgnoreCase(input: string, index: number, value: string): boolean {
  if (index + value.length > input.length) return false;
  return input.slice(index, index + value.length).toLowerCase() === value;
}

function isAtLineStartOrAfterStatementTerminator(input: string, index: number): boolean {
  for (let i = index - 1; i >= 0; i -= 1) {
    const ch = input[i];
    if (ch === '\n' || ch === '\r') return true;
    if (!/\s/.test(ch)) return ch === ';';
  }

  return true;
}

function getLineCommentPrefixLength(input: string, index: number): number {
  const ch = input[index];
  if ((ch === '-' && input[index + 1] === '-') || (ch === '/' && input[index + 1] === '/')) {
    return 2;
  }

  if (!startsWithIgnoreCase(input, index, 'rem')) {
    return 0;
  }

  const next = input[index + 3];
  if (next && !/\s/.test(next)) {
    return 0;
  }

  return isAtLineStartOrAfterStatementTerminator(input, index) ? 3 : 0;
}

export function splitSqlStatements(input: string): string[] {
  if (!input) return [];
  const out: string[] = [];
  let buf = '';
  let i = 0;
  const n = input.length;

  while (i < n) {
    const ch = input[i];

    // 行注释：到行尾
    const lineCommentPrefixLength = getLineCommentPrefixLength(input, i);
    if (lineCommentPrefixLength > 0) {
      buf += input.slice(i, i + lineCommentPrefixLength);
      i += lineCommentPrefixLength;
      while (i < n && input[i] !== '\n' && input[i] !== '\r') {
        buf += input[i];
        i += 1;
      }
      continue;
    }

    // 块注释：到 */（不嵌套）
    if (ch === '/' && input[i + 1] === '*') {
      buf += ch;
      buf += input[i + 1];
      i += 2;
      while (i < n && !(input[i] === '*' && input[i + 1] === '/')) {
        buf += input[i];
        i += 1;
      }
      if (i < n) {
        buf += input[i];
        buf += input[i + 1];
        i += 2;
      }
      continue;
    }

    // 单引号字符串：内部两个单引号 '' 视为转义，不结束字符串
    if (ch === "'") {
      buf += ch;
      i += 1;
      while (i < n) {
        if (input[i] === "'" && input[i + 1] === "'") {
          buf += "''";
          i += 2;
          continue;
        }
        if (input[i] === "'") {
          buf += "'";
          i += 1;
          break;
        }
        buf += input[i];
        i += 1;
      }
      continue;
    }

    // 顶层分号 → 切分
    if (ch === ';') {
      const stmt = buf.trim();
      if (hasSubstantiveContent(stmt)) out.push(stmt);
      buf = '';
      i += 1;
      continue;
    }

    buf += ch;
    i += 1;
  }

  const tail = buf.trim();
  if (hasSubstantiveContent(tail)) out.push(tail);
  return out;
}
