import type MarkdownIt from "npm:markdown-it@14.1.1";
import type { ResolvedDocsThemeOptions } from "./types.ts";
import {
    escapeAttribute,
    escapeHtml,
    parseHeadingAnchor,
    slugAliases,
    slugify,
    uniqueSlug,
} from "./utils.ts";

type MarkdownRenderRule = (
    tokens: any[],
    idx: number,
    options: any,
    env: any,
    self: any,
) => string;

export function markdownOptions(theme: ResolvedDocsThemeOptions) {
    return {
        options: {
            html: true,
            typographer: true,
        },
        plugins: [
            markdownHeadings,
            markdownToc,
            (md: MarkdownIt) => markdownCodeFences(md, theme),
            markdownContainers,
        ],
    };
}

function markdownHeadings(md: MarkdownIt) {
    const renderToken: MarkdownRenderRule = (
        tokens,
        idx,
        options,
        _env,
        self,
    ) => self.renderToken(tokens, idx, options);
    const defaultOpen: MarkdownRenderRule = md.renderer.rules.heading_open ??
        renderToken;
    const defaultClose: MarkdownRenderRule = md.renderer.rules.heading_close ??
        renderToken;

    md.core.ruler.before("normalize", "docs_headings_reset", (state: any) => {
        state.env.__docsHeadingSlugs = [];
    });

    md.renderer.rules.heading_open = ((tokens, idx, options, env, self) => {
        const token = tokens[idx];
        const inline = tokens[idx + 1];
        const parsed = parseHeadingAnchor(inline?.content ?? "");
        if (parsed.id) {
            removeHeadingAnchorSuffix(inline, parsed.text);
        }

        const state = env as { __docsHeadingSlugs?: Array<{ slug: string }> };
        const headings = state.__docsHeadingSlugs ??= [];
        const slug = uniqueSlug(parsed.id ?? slugify(parsed.text), headings);

        token.attrSet("id", slug);
        token.attrSet("tabindex", "-1");
        headings.push({ slug });

        const aliases = slugAliases(slug).filter((alias) =>
            !headings.some((heading) => heading.slug === alias)
        );
        headings.push(...aliases.map((alias) => ({ slug: alias })));
        const aliasHtml = aliases.map((alias) =>
            `<span id="${escapeAttribute(alias)}"></span>`
        ).join("");

        return aliasHtml + defaultOpen(tokens, idx, options, env, self);
    }) as MarkdownRenderRule;

    md.renderer.rules.heading_close = ((tokens, idx, options, env, self) => {
        const open = findMatchingHeadingOpen(tokens, idx);
        const slug = open?.attrGet?.("id") ?? "";
        const title = tokens[idx - 1]?.content ?? "";
        return ` <a class="header-anchor" href="#${
            escapeAttribute(slug)
        }" aria-label="Permalink to ${escapeAttribute(title)}"></a>` +
            defaultClose(tokens, idx, options, env, self);
    }) as MarkdownRenderRule;
}

function removeHeadingAnchorSuffix(inline: any, text: string) {
    if (!inline) {
        return;
    }

    inline.content = text;
    if (!Array.isArray(inline.children)) {
        return;
    }

    for (let index = inline.children.length - 1; index >= 0; index--) {
        const child = inline.children[index];
        if (typeof child?.content !== "string") {
            continue;
        }

        const parsed = parseHeadingAnchor(child.content);
        if (!parsed.id) {
            continue;
        }

        child.content = parsed.text;
        break;
    }
}

function markdownToc(md: MarkdownIt) {
    md.block.ruler.before(
        "paragraph",
        "toc",
        (state: any, startLine: number, _endLine: number, silent: boolean) => {
            const line = containerLine(state, startLine).trim();
            if (line !== "[[toc]]") {
                return false;
            }

            if (silent) {
                return true;
            }

            const token = state.push("html_block", "", 0);
            token.content =
                '<nav class="table-of-contents" data-toc-placeholder></nav>\n';
            token.map = [startLine, startLine + 1];
            state.line = startLine + 1;
            return true;
        },
    );
}

function markdownCodeFences(
    md: MarkdownIt,
    theme: ResolvedDocsThemeOptions,
) {
    const defaultFence = md.renderer.rules.fence!;

    md.renderer.rules.fence = ((tokens, idx, options, env, self) => {
        const token = tokens[idx];
        const parsed = parseFence(token.info, token.content, theme);
        token.info = parsed.language;
        token.content = parsed.content;

        if (parsed.language === "mermaid") {
            return `<pre class="mermaid">${escapeHtml(token.content)}</pre>`;
        }

        const rendered = defaultFence(tokens, idx, options, env, self);
        const language = parsed.language || "text";
        const title = parsed.title
            ? ` data-title="${escapeAttribute(parsed.title)}"`
            : "";
        const lineMeta = parsed.lineMeta
            ? ` data-line-meta="${escapeAttribute(parsed.lineMeta)}"`
            : "";
        const lineNumbers = parsed.lineNumbers
            ? ' data-line-numbers="true"'
            : "";
        const lineNumberStart = parsed.lineNumbers && parsed.lineNumberStart > 1
            ? ` data-line-number-start="${parsed.lineNumberStart}"`
            : "";

        return `<div class="language-${
            escapeAttribute(language)
        }"${title}${lineMeta}${lineNumbers}${lineNumberStart}><button title="Copy Code" class="copy" type="button"></button><span class="lang">${
            escapeHtml(language)
        }</span>${rendered}</div>`;
    }) as MarkdownRenderRule;
}

function markdownContainers(md: MarkdownIt) {
    addContainer(md, "tip", customBlock("tip", "TIP"));
    addContainer(md, "info", customBlock("info", "INFO"));
    addContainer(md, "warning", customBlock("warning", "WARNING"));
    addContainer(md, "danger", customBlock("danger", "DANGER"));
    addContainer(md, "details", detailsBlock());
    addContainer(md, "code-group", codeGroupBlock());
    addRawContainer(md);
}

type ContainerRenderer = {
    open: (info: string) => string;
    close: () => string;
};

function addContainer(
    md: MarkdownIt,
    name: string,
    renderer: ContainerRenderer,
) {
    md.block.ruler.before(
        "fence",
        `container_${name}`,
        (
            state: any,
            startLine: number,
            endLine: number,
            silent: boolean,
        ) => {
            const line = containerLine(state, startLine);
            const match =
                /^(?<marker>:{3,})\s*(?<name>[A-Za-z][\w-]*)(?<info>.*)$/
                    .exec(line);

            if (!match?.groups || match.groups.name !== name) {
                return false;
            }

            if (silent) {
                return true;
            }

            const markerLength = match.groups.marker.length;
            let nextLine = startLine;
            let autoClosed = false;

            for (nextLine = startLine + 1; nextLine < endLine; nextLine++) {
                const currentLine = containerLine(state, nextLine);
                if (new RegExp(`^:{${markerLength},}\\s*$`).test(currentLine)) {
                    autoClosed = true;
                    break;
                }
            }

            const openToken = state.push(`container_${name}_open`, "div", 1);
            openToken.markup = match.groups.marker;
            openToken.info = match.groups.info.trim();
            openToken.block = true;
            openToken.map = [startLine, nextLine];

            state.md.block.tokenize(state, startLine + 1, nextLine);

            const closeToken = state.push(`container_${name}_close`, "div", -1);
            closeToken.markup = match.groups.marker;
            closeToken.block = true;

            state.line = nextLine + (autoClosed ? 1 : 0);
            return true;
        },
        { alt: ["paragraph", "reference", "blockquote", "list"] },
    );

    md.renderer.rules[`container_${name}_open`] =
        ((tokens, idx) =>
            renderer.open(
                (tokens[idx] as any).info ?? "",
            )) as MarkdownRenderRule;
    md.renderer.rules[`container_${name}_close`] = () => renderer.close();
}

function addRawContainer(md: MarkdownIt) {
    md.block.ruler.before(
        "fence",
        "container_raw",
        (
            state: any,
            startLine: number,
            endLine: number,
            silent: boolean,
        ) => {
            const line = containerLine(state, startLine);
            const match = /^(?<marker>:{3,})\s*raw\s*$/.exec(line);
            if (!match?.groups) {
                return false;
            }

            if (silent) {
                return true;
            }

            const markerLength = match.groups.marker.length;
            let nextLine = startLine;
            let autoClosed = false;

            for (nextLine = startLine + 1; nextLine < endLine; nextLine++) {
                const currentLine = containerLine(state, nextLine);
                if (new RegExp(`^:{${markerLength},}\\s*$`).test(currentLine)) {
                    autoClosed = true;
                    break;
                }
            }

            const token = state.push("html_block", "", 0);
            token.content = state.getLines(
                startLine + 1,
                nextLine,
                state.blkIndent,
                false,
            );
            token.map = [startLine, nextLine];
            state.line = nextLine + (autoClosed ? 1 : 0);
            return true;
        },
        { alt: ["paragraph", "reference", "blockquote"] },
    );
}

function containerLine(state: any, line: number): string {
    const start = state.bMarks[line] + state.tShift[line];
    const end = state.eMarks[line];
    return state.src.slice(start, end);
}

function customBlock(type: string, fallbackTitle: string) {
    return {
        open(info: string) {
            const title = info || fallbackTitle;
            return `<div class="${type} custom-block"><p class="custom-block-title">${
                escapeHtml(title)
            }</p>\n`;
        },
        close() {
            return "</div>\n";
        },
    };
}

function detailsBlock() {
    return {
        open(info: string) {
            const open = /(^|\s)open(\s|$)/i.test(info);
            const title = info.replace(/(^|\s)open(\s|$)/i, " ").trim() ||
                "Details";
            return `<details class="details custom-block"${
                open ? " open" : ""
            }><summary>${escapeHtml(title)}</summary>\n`;
        },
        close() {
            return "</details>\n";
        },
    };
}

function codeGroupBlock() {
    return {
        open() {
            return '<div class="vp-code-group">\n';
        },
        close() {
            return "</div>\n";
        },
    };
}

function parseFence(
    info: string,
    content: string,
    theme: ResolvedDocsThemeOptions,
) {
    const trimmed = info.trim();
    const match = /^(?<language>[^\s{\[]+)?(?<meta>[\s\S]*)$/.exec(trimmed);
    const languageParts = splitLanguageMeta(match?.groups?.language ?? "");
    const meta = `${languageParts.meta} ${match?.groups?.meta ?? ""}`.trim();
    const title = /\[([^\]]+)]/.exec(meta)?.[1]?.trim();
    const range = /\{([^}]+)}/.exec(meta)?.[1]?.trim();
    const markerResult = extractCodeMarkers(content);
    const lineClasses = markerResult.lineClasses;

    if (range) {
        for (const line of parseLineRanges(range)) {
            addLineClass(lineClasses, line, "highlighted");
        }
    }

    const lineNumberMatch = /(?:^|\s|:)line-numbers(?:=(\d+))?/i.exec(meta);
    const noLineNumbers = /(?:^|\s|:)no-line-numbers\b/i.test(meta);

    return {
        language: normalizeLanguage(languageParts.language),
        title,
        content: markerResult.content,
        lineNumbers: noLineNumbers
            ? false
            : theme.code.lineNumbers || Boolean(lineNumberMatch),
        lineNumberStart: Math.max(1, Number(lineNumberMatch?.[1] ?? 1)),
        lineMeta: serializeLineClasses(lineClasses),
    };
}

function splitLanguageMeta(value: string) {
    const separator = value.indexOf(":");
    if (separator === -1) {
        return { language: value, meta: "" };
    }

    return {
        language: value.slice(0, separator),
        meta: value.slice(separator),
    };
}
function extractCodeMarkers(content: string) {
    const lineClasses = new Map<number, Set<string>>();
    const lines = content.replace(/\r\n/g, "\n").split("\n");
    const marker =
        /\s*(?:\/\/|#|<!--|\/\*|\*)?\s*\[!code\s+(highlight|focus|warning|error|\+\+|--)]\s*(?:-->|\*\/)?/gi;

    const cleaned = lines.map((line, index) => {
        let result = line;
        for (const match of line.matchAll(marker)) {
            addLineClass(lineClasses, index + 1, codeMarkerClass(match[1]));
            result = result.replace(match[0], "").replace(/\s+$/, "");
        }
        return result;
    });

    return {
        content: cleaned.join("\n"),
        lineClasses,
    };
}

function codeMarkerClass(value: string) {
    switch (value) {
        case "++":
            return "diff add";
        case "--":
            return "diff remove";
        case "focus":
            return "has-focus";
        default:
            return value;
    }
}

function parseLineRanges(value: string) {
    const lines = new Set<number>();
    for (const part of value.split(",")) {
        const match = /^(\d+)(?:-(\d+))?$/.exec(part.trim());
        if (!match) {
            continue;
        }

        const start = Number(match[1]);
        const end = Number(match[2] ?? match[1]);
        for (let line = start; line <= end; line++) {
            lines.add(line);
        }
    }

    return lines;
}

function addLineClass(
    lineClasses: Map<number, Set<string>>,
    line: number,
    className: string,
) {
    const existing = lineClasses.get(line) ?? new Set<string>();
    for (const name of className.split(" ")) {
        existing.add(name);
    }
    lineClasses.set(line, existing);
}

function serializeLineClasses(lineClasses: Map<number, Set<string>>) {
    if (lineClasses.size === 0) {
        return "";
    }

    return [...lineClasses.entries()]
        .sort(([left], [right]) => left - right)
        .map(([line, classes]) => `${line}:${[...classes].join(",")}`)
        .join(";");
}

function normalizeLanguage(language: string): string {
    return language.trim().toLowerCase().replace(/[^a-z0-9_-]/g, "");
}

function findMatchingHeadingOpen(tokens: unknown[], closeIndex: number) {
    for (let index = closeIndex - 1; index >= 0; index--) {
        const token = tokens[index] as {
            type?: string;
            attrGet?: (name: string) => string | null;
        };
        if (token.type === "heading_open") {
            return token;
        }
    }

    return undefined;
}
