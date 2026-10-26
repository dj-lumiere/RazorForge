import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";

/**
 * The run buttons: build and run the file in the editor, or the solution its folder belongs to (the nearest
 * config.toml above the file, else the workspace folder). Both run in one terminal per language, which is reused, so
 * the program's input and output stay in the editor.
 */
export function registerRun(
    context: vscode.ExtensionContext,
    language: { id: string; name: string },
    builder: () => string | undefined,
): void {
    context.subscriptions.push(
        vscode.commands.registerCommand(`${language.id}.runFile`, (uri?: vscode.Uri) => runFile(language, builder, uri)),
        vscode.commands.registerCommand(`${language.id}.runFolder`, (uri?: vscode.Uri) => runFolder(language, builder, uri)),
    );
}

async function runFile(language: { id: string; name: string }, builder: () => string | undefined, uri?: vscode.Uri) {
    const file = await target(uri);
    if (!file) return;
    await launch(language, builder, path.dirname(file), ["run", file]);
}

async function runFolder(language: { id: string; name: string }, builder: () => string | undefined, uri?: vscode.Uri) {
    const file = await target(uri);
    const start = file ? path.dirname(file) : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!start) return;
    const root = solutionAbove(start) ?? vscode.workspace.getWorkspaceFolder(vscode.Uri.file(start))?.uri.fsPath ?? start;
    await launch(language, builder, root, ["run"]);
}

/** The file to run: the one the button was pressed on, else the active editor's. Unsaved changes are saved first. */
async function target(uri?: vscode.Uri): Promise<string | undefined> {
    const document = uri
        ? vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString())
        : vscode.window.activeTextEditor?.document;
    if (document?.isDirty) await document.save();
    const chosen = uri ?? document?.uri;
    if (!chosen || chosen.scheme !== "file") {
        void vscode.window.showWarningMessage(vscode.env.language.startsWith("ko")
            ? "실행할 파일이 없습니다. 파일을 저장한 뒤 다시 실행하세요."
            : "There is no file to run. Save the file, then run it again.");
        return undefined;
    }
    return chosen.fsPath;
}

/** The nearest folder at or above `start` that holds a config.toml. */
function solutionAbove(start: string): string | undefined {
    for (let dir = start; ; dir = path.dirname(dir)) {
        if (fs.existsSync(path.join(dir, "config.toml"))) return dir;
        if (path.dirname(dir) === dir) return undefined;
    }
}

async function launch(language: { id: string; name: string }, builder: () => string | undefined, cwd: string, args: string[]) {
    const tool = builder();
    if (!tool) {
        void vscode.window.showErrorMessage(vscode.env.language.startsWith("ko")
            ? `${language.name} 빌더를 찾지 못했습니다. 설정에 경로를 지정하거나 ${language.name}을(를) 빌드하세요.`
            : `The ${language.name} builder isn't built or set. Set its path in the settings, or build ${language.name}.`);
        return;
    }
    const command = tool.endsWith(".dll") ? ["dotnet", tool, ...args] : [tool, ...args];
    const name = `${language.name}: run`;
    let terminal = vscode.window.terminals.find(t => t.name === name && t.exitStatus === undefined);
    if (!terminal) terminal = vscode.window.createTerminal({ name, cwd });
    terminal.show(true);
    terminal.sendText(`cd ${quote(cwd)} && ${command.map(quote).join(" ")}`);
}

function quote(text: string): string {
    if (process.platform === "win32") return `"${text.replace(/"/g, '\\"')}"`;
    return /^[\w@%+=:,./-]+$/.test(text) ? text : `'${text.replace(/'/g, `'\\''`)}'`;
}
