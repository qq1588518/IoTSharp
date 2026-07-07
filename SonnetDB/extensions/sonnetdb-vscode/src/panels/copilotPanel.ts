import * as vscode from 'vscode';

export class CopilotPanel {
  public show(): void {
    const panel = vscode.window.createWebviewPanel(
      'sonnetdb.copilot',
      'SonnetDB Copilot',
      vscode.ViewColumn.Beside,
      {
        enableScripts: false,
      },
    );

    panel.webview.html = `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>SonnetDB Copilot</title>
    <style>
      body {
        font-family: ui-sans-serif, system-ui, sans-serif;
        padding: 16px;
      }
    </style>
  </head>
  <body>
    <h2>SonnetDB Copilot</h2>
    <p>This is a placeholder panel. PR #104 will connect it to /v1/copilot/chat/stream.</p>
  </body>
</html>`;
  }
}
