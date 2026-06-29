import {
    dirname,
    extname,
    isAbsolute,
    normalize,
    relative,
    resolve,
} from "node:path";
import type { Page } from "lume/core/file.ts";

import type { ResolvedDocsThemeOptions } from "./types.ts";
import { sourcePath } from "./utils.ts";

const snippetPattern = /^<<<\s+(.+?)\s*$/gm;
const includePattern = /<!--\s*@include:\s+(.+?)\s*-->/g;

export function prepareMarkdownSource(
    page: Page,
    theme: ResolvedDocsThemeOptions,
) {
    if (!theme.snippets) {
        page.text = expandGithubAlerts(page.text);
        return;
    }

    const pagePath = sourcePath(page);
    const withIncludes = expandMarkdownIncludes(page.text, pagePath, theme);
    const withSnippets = expandCodeSnippets(withIncludes, pagePath, theme);
    page.text = expandGithubAlerts(withSnippets);
}

function expandMarkdownIncludes(
    markdown: string,
    pagePath: string,
    theme: ResolvedDocsThemeOptions,
) {
    return markdown.replace(includePattern, (_match, request: string) => {
        const resolved = resolveRequest(request, pagePath, theme);
        let content = readRequestedLines(resolved).content;
        content = stripFrontMatter(content).trim();
        return content ? `${content}\n` : "";
    });
}

function expandCodeSnippets(
    markdown: string,
    pagePath: string,
    theme: ResolvedDocsThemeOptions,
) {
    return markdown.replace(snippetPattern, (_match, request: string) => {
        const resolved = resolveRequest(request, pagePath, theme);
        const { content, displayPath } = readRequestedLines(resolved);
        const language = resolved.language || languageFromPath(displayPath);
        const title = resolved.title ?? displayPath.replaceAll("\\", "/");
        const fence = codeFenceFor(content);
        const meta = [language, title ? `[${title}]` : ""].filter(Boolean).join(
            " ",
        );

        return `${fence}${meta}\n${content.replace(/\s+$/, "")}\n${fence}`;
    });
}

type ResolvedRequest = {
    filePath: string;
    displayPath: string;
    range?: LineRange;
    title?: string;
    language?: string;
};

type LineRange = {
    start: number;
    end: number;
};

function resolveRequest(
    request: string,
    pagePath: string,
    theme: ResolvedDocsThemeOptions,
): ResolvedRequest {
    const snippets = theme.snippets;
    if (!snippets) {
        throw new Error("Snippet support is disabled.");
    }

    let value = request.trim();
    let language: string | undefined;
    let title: string | undefined;
    let range: LineRange | undefined;

    const titleMatch = /\s+\[([^\]]+)]\s*$/.exec(value);
    if (titleMatch) {
        title = titleMatch[1].trim();
        value = value.slice(0, titleMatch.index).trim();
    }

    const trailingLanguage = /\s+lang=([A-Za-z0-9_-]+)\s*$/.exec(value);
    if (trailingLanguage) {
        language = trailingLanguage[1];
        value = value.slice(0, trailingLanguage.index).trim();
    }

    const rangeMatch = /\{([^}]+)}\s*$/.exec(value);
    if (rangeMatch) {
        range = parseLineRange(rangeMatch[1]);
        value = value.slice(0, rangeMatch.index).trim();
    }

    const hashRange = /#L(\d+)(?:-L?(\d+))?$/.exec(value);
    if (hashRange) {
        range = {
            start: Number(hashRange[1]),
            end: Number(hashRange[2] ?? hashRange[1]),
        };
        value = value.slice(0, hashRange.index).trim();
    }

    const docsRoot = resolve(Deno.cwd(), snippets.docsRoot);
    const repoRoot = resolve(Deno.cwd(), snippets.repoRoot);
    const absolutePagePath = resolve(
        Deno.cwd(),
        pagePath.replace(/^\/+/, ""),
    );

    const alias = Object.entries(snippets.aliases).find(([prefix]) =>
        value.startsWith(prefix)
    );
    const resolved = alias
        ? resolve(Deno.cwd(), alias[1], value.slice(alias[0].length))
        : value.startsWith("@/")
        ? resolve(docsRoot, value.slice(2))
        : value.startsWith("~/")
        ? resolve(repoRoot, value.slice(2))
        : resolve(dirname(absolutePagePath), value);

    const filePath = normalize(resolved);
    if (!isInside(filePath, repoRoot) && !isInside(filePath, docsRoot)) {
        throw new Error(
            `Refusing to read ${value}; snippets must stay under ${repoRoot} or ${docsRoot}.`,
        );
    }

    return {
        filePath,
        displayPath: normalize(relative(repoRoot, filePath)).replaceAll(
            "\\",
            "/",
        ),
        range,
        title,
        language,
    };
}

function readRequestedLines(request: ResolvedRequest) {
    const content = Deno.readTextFileSync(request.filePath);
    if (!request.range) {
        return { content, displayPath: request.displayPath };
    }

    const lines = content.replace(/\r\n/g, "\n").split("\n");
    const start = Math.max(request.range.start, 1);
    const end = Math.max(request.range.end, start);
    return {
        content: lines.slice(start - 1, end).join("\n"),
        displayPath: `${request.displayPath}:${start}-${end}`,
    };
}

function parseLineRange(value: string): LineRange {
    const match = /^(\d+)(?:[-,](\d+))?/.exec(value.trim());
    if (!match) {
        throw new Error(`Invalid snippet range: ${value}`);
    }

    return {
        start: Number(match[1]),
        end: Number(match[2] ?? match[1]),
    };
}

function expandGithubAlerts(markdown: string) {
    const lines = markdown.replace(/\r\n/g, "\n").split("\n");
    const output: string[] = [];

    for (let index = 0; index < lines.length; index++) {
        const alert = /^>\s*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)]\s*(.*)$/i
            .exec(lines[index]);
        if (!alert) {
            output.push(lines[index]);
            continue;
        }

        const type = alertType(alert[1]);
        const title = alert[2]?.trim() || alertTitle(alert[1]);
        const body: string[] = [];

        for (index = index + 1; index < lines.length; index++) {
            const quoted = /^>\s?(.*)$/.exec(lines[index]);
            if (!quoted) {
                index--;
                break;
            }
            body.push(quoted[1]);
        }

        output.push(`::: ${type} ${title}`);
        output.push(...body);
        output.push(":::");
    }

    return output.join("\n");
}

function alertType(value: string) {
    switch (value.toUpperCase()) {
        case "NOTE":
            return "info";
        case "WARNING":
            return "warning";
        case "CAUTION":
            return "danger";
        default:
            return "tip";
    }
}

function alertTitle(value: string) {
    const normalized = value.toUpperCase();
    return normalized === "CAUTION" ? "CAUTION" : normalized;
}

function stripFrontMatter(markdown: string) {
    return markdown.replace(/^---\s*\n[\s\S]*?\n---\s*\n?/, "");
}

function codeFenceFor(content: string) {
    const longest = Math.max(
        2,
        ...Array.from(content.matchAll(/`+/g), (match) => match[0].length),
    );
    return "`".repeat(longest + 1);
}

function languageFromPath(path: string) {
    const extension = extname(path).toLowerCase().replace(/^\./, "");
    return languageByExtension[extension] ?? extension;
}

function isInside(path: string, root: string) {
    if (path === root) {
        return true;
    }

    const child = relative(root, path);
    return child !== "" && !child.startsWith("..") && !isAbsolute(child);
}

const languageByExtension: Record<string, string> = {
    cs: "csharp",
    csproj: "xml",
    props: "xml",
    targets: "xml",
    fs: "fsharp",
    js: "javascript",
    jsx: "jsx",
    ts: "typescript",
    tsx: "tsx",
    md: "markdown",
    yml: "yaml",
    yaml: "yaml",
    ps1: "powershell",
    sh: "bash",
    html: "html",
    css: "css",
    json: "json",
    xml: "xml",
};
