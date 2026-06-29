import type { Page } from "lume/core/file.ts";
import { createHighlighter } from "shiki";
import type { ResolvedDocsThemeOptions } from "./types.ts";

let highlighterPromise: ReturnType<typeof createHighlighter> | undefined;

export async function highlightCodeBlocks(
    pages: Page[],
    theme: ResolvedDocsThemeOptions,
) {
    if (!theme.codeHighlight) {
        return;
    }

    const highlighter = await getHighlighter(theme);
    for (const page of pages) {
        for (
            const wrapper of page.document.querySelectorAll<HTMLElement>(
                "div[class*='language-']",
            )
        ) {
            const pre = wrapper.querySelector<HTMLElement>("pre");
            const code = wrapper.querySelector<HTMLElement>("pre > code");
            if (!pre || !code) {
                continue;
            }

            const language = shikiLanguage(wrapper.className, theme);
            const source = (code.textContent ?? "").replace(/\n$/, "");
            const highlighted = highlighter.codeToHtml(source, {
                lang: language,
                themes: theme.codeHighlight.themes,
                defaultColor: false,
            });

            pre.outerHTML = highlighted;
        }
    }
}

export function applyCodeLineAnnotations(page: Page) {
    for (
        const wrapper of page.document.querySelectorAll<HTMLElement>(
            "div[class*='language-']",
        )
    ) {
        const code = wrapper.querySelector<HTMLElement>("pre > code");
        const pre = wrapper.querySelector<HTMLElement>("pre");
        if (!code || !pre) {
            continue;
        }

        const metadata = parseLineMetadata(
            wrapper.getAttribute("data-line-meta") ?? "",
        );
        const lineNumbers = wrapper.getAttribute("data-line-numbers") ===
            "true";
        const lineNumberStart = Math.max(
            1,
            Number(wrapper.getAttribute("data-line-number-start") ?? 1),
        );
        const shikiLines = [...code.querySelectorAll<HTMLElement>(".line")];
        const lineCount = shikiLines.length ||
            code.innerHTML.split("\n").length;
        const hasFocusedLines = [...metadata.values()].some((classes) =>
            classes.includes("has-focus")
        );

        if (shikiLines.length > 0) {
            shikiLines.forEach((line, index) => {
                applyLineMetadata(
                    line,
                    index + 1,
                    lineNumberStart + index,
                    metadata,
                );
            });
            code.innerHTML = shikiLines.map((line) => line.outerHTML).join("");
        } else {
            const original = code.innerHTML.replace(/\n$/, "");
            code.innerHTML = original.split("\n").map((line, index) => {
                const sourceLineNumber = index + 1;
                const displayLineNumber = lineNumberStart + index;
                const classes = [
                    "line",
                    ...(metadata.get(sourceLineNumber) ?? []),
                ].join(" ");
                return `<span class="${classes}" data-line-number="${displayLineNumber}">${
                    line || "\u200b"
                }</span>`;
            }).join("");
        }

        wrapper.classList.add("has-lines");
        if (hasFocusedLines) {
            pre.classList.add("has-focused-lines");
        }

        if (lineNumbers) {
            wrapper.classList.add("line-numbers-mode");
            if (!wrapper.querySelector(".line-numbers-wrapper")) {
                wrapper.insertAdjacentHTML(
                    "beforeend",
                    `<div class="line-numbers-wrapper" aria-hidden="true">${
                        lineNumbersHtml(lineCount, lineNumberStart)
                    }</div>`,
                );
            }
        }

        wrapper.removeAttribute("data-line-meta");
        wrapper.removeAttribute("data-line-numbers");
        wrapper.removeAttribute("data-line-number-start");
    }

    initializeCodeGroups(page);
}

function getHighlighter(theme: ResolvedDocsThemeOptions) {
    if (!theme.codeHighlight) {
        throw new Error("Code highlighting is disabled.");
    }

    highlighterPromise ??= createHighlighter({
        themes: [
            theme.codeHighlight.themes.light,
            theme.codeHighlight.themes.dark,
        ],
        langs: theme.codeHighlight.langs,
    });

    return highlighterPromise;
}

function shikiLanguage(
    className: string,
    theme: ResolvedDocsThemeOptions,
): string {
    if (!theme.codeHighlight) {
        return "text";
    }

    const raw = /language-([\w-]+)/.exec(className)?.[1]?.toLowerCase() ??
        "text";
    const normalized = languageAliases[raw] ?? raw;
    return theme.codeHighlight.langs.includes(normalized) ? normalized : "text";
}

function applyLineMetadata(
    line: HTMLElement,
    sourceLineNumber: number,
    displayLineNumber: number,
    metadata: Map<number, string[]>,
) {
    line.setAttribute("data-line-number", String(displayLineNumber));

    const classes = metadata.get(sourceLineNumber) ?? [];
    if (classes.length > 0) {
        line.classList.add(...classes);
    }

    if (line.innerHTML === "") {
        line.innerHTML = "\u200b";
    }
}

function parseLineMetadata(value: string) {
    const result = new Map<number, string[]>();
    for (const item of value.split(";")) {
        if (!item.trim()) {
            continue;
        }

        const [lineValue, classValue = ""] = item.split(":", 2);
        const line = Number(lineValue);
        const classes = classValue.split(",").map((part) => part.trim())
            .filter(Boolean);
        if (Number.isFinite(line) && classes.length > 0) {
            result.set(line, classes);
        }
    }

    return result;
}

function lineNumbersHtml(count: number, start: number) {
    return Array.from(
        { length: count },
        (_value, index) =>
            `<span class="line-number">${start + index}</span><br>`,
    ).join("");
}

function initializeCodeGroups(page: Page) {
    for (
        const group of page.document.querySelectorAll<HTMLElement>(
            ".vp-code-group",
        )
    ) {
        const blocks = [...group.children].filter((
            child,
        ): child is HTMLElement =>
            child.matches("div[class^='language-'], div[class*=' language-']")
        );

        blocks.forEach((block, index) => {
            const active = index === 0;
            block.classList.toggle("active", active);
            if (active) {
                block.removeAttribute("hidden");
            } else {
                block.setAttribute("hidden", "");
            }
        });
    }
}

const languageAliases: Record<string, string> = {
    bash: "shellscript",
    c: "csharp",
    cs: "csharp",
    javascript: "javascript",
    js: "javascript",
    md: "markdown",
    ps1: "powershell",
    pwsh: "powershell",
    sh: "shellscript",
    shell: "shellscript",
    text: "text",
    ts: "typescript",
    txt: "text",
    xml: "xml",
    yaml: "yaml",
    yml: "yaml",
    zsh: "shellscript",
};
