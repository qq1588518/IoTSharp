import * as vscode from 'vscode';
import { SonnetDbClient } from '../core/sonnetdbClient';
import { SonnetDbConnectionProfile } from '../core/types';
import { QueryResultPanel } from '../panels/queryResultPanel';

export function registerRunQueryCommand(
  context: vscode.ExtensionContext,
  getActiveProfile: () => SonnetDbConnectionProfile | undefined,
  getToken: (profile: SonnetDbConnectionProfile) => Promise<string | undefined>,
  resultPanel: QueryResultPanel,
): void {
  const disposable = vscode.commands.registerCommand('sonnetdb.runQuery', async () => {
    const profile = getActiveProfile();
    if (!profile) {
      void vscode.window.showWarningMessage('No active SonnetDB connection is selected yet.');
      return;
    }

    const database = await vscode.window.showInputBox({
      prompt: 'Target database',
      value: profile.defaultDatabase ?? '',
      ignoreFocusOut: true,
    });

    if (!database) {
      return;
    }

    const sql = await vscode.window.showInputBox({
      prompt: 'SQL to execute',
      ignoreFocusOut: true,
      value: 'SELECT * FROM cpu LIMIT 10',
    });

    if (!sql) {
      return;
    }

    const token = await getToken(profile);
    const client = new SonnetDbClient(profile.baseUrl, token);

    try {
      const result = await client.executeSql(database, sql);
      resultPanel.show(result, sql);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      void vscode.window.showErrorMessage(`SonnetDB query failed: ${message}`);
    }
  });

  context.subscriptions.push(disposable);
}
