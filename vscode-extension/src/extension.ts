import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions } from "vscode-languageclient/node";

/**
 * RazorForge in VS Code: the TextMate grammar colors the text, and the language server (`RazorForge lsp`) brings the errors,
 * semantic colors, hover, completion, go to definition, rename and formatting. The server runs from a copy of its
 * build folder, so it never holds the files a rebuild overwrites, and a rebuild restarts it.
 */
let client: LanguageClient | undefined;
let watcher: vscode.FileSystemWatcher | undefined;
let restartTimer: NodeJS.Timeout | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    context.subscriptions.push(
        vscode.commands.registerCommand("razorforge.restartServer", () => restart(context)),
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration("razorforge.serverPath")) void restart(context);
        }),
    );
    await start(context);
}

export async function deactivate(): Promise<void> {
    watcher?.dispose();
    await client?.stop();
}

async function restart(context: vscode.ExtensionContext): Promise<void> {
    watcher?.dispose();
    await client?.stop();
    client = undefined;
    await start(context);
}

async function start(context: vscode.ExtensionContext): Promise<void> {
    const server = locateServer();
    if (!server) {
        void vscode.window.showErrorMessage(
            vscode.env.language.startsWith("ko")
                ? "RazorForge 언어 서버를 찾지 못했습니다. 설정의 razorforge.serverPath에 지정하거나 RazorForge를 빌드하세요."
                : "The RazorForge language server isn't built or set. Set razorforge.serverPath, or build RazorForge.");
        return;
    }
    const staged = server.endsWith(".dll") ? stage(context, server) : server;
    const serverOptions: ServerOptions = {
        command: staged.endsWith(".dll") ? "dotnet" : staged,
        args: staged.endsWith(".dll") ? [staged, "lsp"] : ["lsp"],
        options: {
            cwd: path.dirname(staged),
            env: {
                ...process.env,
                // Hover and doc text in the editor's language.
                LSP_LOCALE: vscode.env.language,
        // It names a stdlib location by the source the build copied it from, found from the build folder.
        ANVILA_BUILD_DIR: path.dirname(server),
            },
        },
    };
    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: "file", language: "razorforge" }, { scheme: "untitled", language: "razorforge" }],
    };
    client = new LanguageClient("razorforge", "RazorForge", serverOptions, clientOptions);
    await client.start();
    watchBuild(context, path.dirname(server));
}

/** The server the setting names, else the dev build in a workspace folder, else `RazorForge` on the PATH. */
function locateServer(): string | undefined {
    const set = vscode.workspace.getConfiguration("razorforge").get<string>("serverPath")?.trim();
    if (set) return fs.existsSync(set) ? set : undefined;
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
        for (const candidate of [
            path.join(folder.uri.fsPath, "RazorForge", "bin", "Debug", "net10.0", "RazorForge.dll"),
            path.join(folder.uri.fsPath, "bin", "Debug", "net10.0", "RazorForge.dll"),
        ]) {
            if (fs.existsSync(candidate)) return candidate;
        }
    }
    return onPath("RazorForge");
}

function onPath(name: string): string | undefined {
    const extensions = process.platform === "win32" ? [".exe", ".cmd", ""] : [""];
    for (const dir of (process.env.PATH ?? "").split(path.delimiter)) {
        for (const ext of extensions) {
            const candidate = path.join(dir, name + ext);
            if (fs.existsSync(candidate)) return candidate;
        }
    }
    return undefined;
}

/** A copy of the server's folder in the extension's storage, one per build of it, so a rebuild can overwrite the original. */
function stage(context: vscode.ExtensionContext, server: string): string {
    const folder = path.dirname(server);
    const stamp = String(newest(folder));
    const root = path.join(context.globalStorageUri.fsPath, "razorforge-lsp");
    const target = path.join(root, stamp);
    if (!fs.existsSync(path.join(target, path.basename(server)))) {
        fs.mkdirSync(root, { recursive: true });
        fs.cpSync(folder, target, { recursive: true });
    }
    // Older copies go, when nothing holds them any more.
    for (const old of fs.readdirSync(root)) {
        if (old === stamp) continue;
        try { fs.rmSync(path.join(root, old), { recursive: true, force: true }); } catch { /* still running */ }
    }
    return path.join(target, path.basename(server));
}

/** The newest file time in a folder: it changes whenever a rebuild writes into it. */
function newest(folder: string): number {
    let latest = 0;
    for (const entry of fs.readdirSync(folder, { withFileTypes: true, recursive: true })) {
        if (!entry.isFile()) continue;
        const time = fs.statSync(path.join(entry.parentPath, entry.name)).mtimeMs;
        if (time > latest) latest = time;
    }
    return Math.floor(latest);
}

/** Restarts the server when its build folder changes and then stays the same for a few seconds. */
function watchBuild(context: vscode.ExtensionContext, folder: string): void {
    watcher?.dispose();
    watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(folder), "*.dll"));
    const changed = () => {
        if (restartTimer) clearTimeout(restartTimer);
        restartTimer = setTimeout(() => void restart(context), 3000);
    };
    watcher.onDidChange(changed);
    watcher.onDidCreate(changed);
    context.subscriptions.push(watcher);
}
