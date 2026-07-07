import * as vscode from 'vscode';
import { SqlResultSet } from '../core/types';

export class QueryResultPanel {
  public show(result: SqlResultSet, sql: string): void {
    const panel = vscode.window.createWebviewPanel(
      'sonnetdb.queryResult',
      'SonnetDB Query Result',
      vscode.ViewColumn.Beside,
      {
        enableScripts: false,
      },
    );

    panel.webview.html = this.renderHtml(result, sql);
  }

  private renderHtml(result: SqlResultSet, sql: string): string {
    const payload = escapeHtml(JSON.stringify(result, null, 2));
    const statement = escapeHtml(sql);

    return `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>SonnetDB Query Result</title>
    <style>
      body {
        font-family: ui-sans-serif, system-ui, sans-serif;
        padding: 16px;
      }
      pre {
        white-space: pre-wrap;
        word-break: break-word;
        background: #f5f7fa;
        padding: 12px;
        border-radius: 8px;
      }
      code {
        font-family: ui-monospace, monospace;
      }
    </style>
  </head>
  <body>
    <h2>Query</h2>
    <pre><code>${statement}</code></pre>
    <h2>Raw Result</h2>
    <pre>${payload}</pre>
    <p>This panel is a placeholder. PR #103 will replace it with table, raw, and chart tabs.</p>
  </body>
</html>`;
  }
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/gu, '&amp;')
    .replace(/</gu, '&lt;')
    .replace(/>/gu, '&gt;');
}
