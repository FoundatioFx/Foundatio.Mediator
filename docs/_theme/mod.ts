import type Site from "lume/core/site.ts";
import { Page } from "lume/core/file.ts";
import basePath from "lume/plugins/base_path.ts";
import checkUrls from "lume/plugins/check_urls.ts";
import markdown from "lume/plugins/markdown.ts";
import metas from "lume/plugins/metas.ts";
import sitemap from "lume/plugins/sitemap.ts";

import { prepareMarkdownSource } from "./authoring.ts";
import { applyCodeLineAnnotations, highlightCodeBlocks } from "./code.ts";
import { markdownOptions } from "./markdown.ts";
import {
    buildSidebar,
    docsSidebarPages,
    flattenSidebar,
} from "./navigation.ts";
import {
    buildSearchIndex,
    renderDocument,
    renderLlmsFull,
    renderLlmsIndex,
    renderMarkdownMirror,
    renderNotFound,
} from "./render.ts";
import type {
    ContentEntry,
    DocsData,
    DocsThemeOptions,
    ResolvedDocsThemeOptions,
    ThemeLabels,
} from "./types.ts";
import {
    cleanPageUrl,
    docsUrlPrefix,
    extractHeadings,
    isDocsPath,
    isMarkdownPage,
    legacyHtmlUrl,
    normalizeDocsRoot,
    normalizeInternalLinks,
    sourcePath,
    transformBadgeTags,
} from "./utils.ts";

export type { DocsThemeOptions } from "./types.ts";

export default function docsTheme(options: DocsThemeOptions) {
    const theme = resolveOptions(options);

    return (site: Site) => {
        applyDocsTheme(site, theme);
    };
}

function applyDocsTheme(
    site: Site,
    theme: ResolvedDocsThemeOptions,
) {
    let searchContentPages: ContentEntry[] = [];

    site.ignore(...theme.ignore);
    site.use(markdown(markdownOptions(theme)));

    if (theme.rawPagesDir && directoryExists(theme.rawPagesDir)) {
        site.copy(theme.rawPagesDir, ".");
    }

    if (theme.assets) {
        site.add(import.meta.resolve("./assets/site.css"), "/assets/site.css");
        site.add(import.meta.resolve("./assets/site.js"), "/assets/site.js");
        site.add(
            import.meta.resolve("./assets/inter-roman-latin.woff2"),
            "/assets/inter-roman-latin.woff2",
        );
    }

    site.preprocess([".md"], (pages) => {
        for (const page of pages) {
            const data = page.data as DocsData;
            normalizeLayout(data);
            applyLastUpdated(page, theme);
            prepareMarkdownSource(page, theme);
        }
    });

    site.process([".html"], async (pages, allPages) => {
        const contentPages: ContentEntry[] = pages
            .filter((page) => isMarkdownPage(page))
            .map((page) => {
                const data = page.data as DocsData;
                data.url = cleanPageUrl(data.url);
                markPageAsHtml(page);
                const markdown = readOriginalSource(page);
                const html = transformBadgeTags(
                    normalizeInternalLinks(page.text, data.url),
                );
                data.headings = extractHeadings(html);
                return {
                    page,
                    data,
                    sourcePath: sourcePath(page),
                    html,
                    markdown,
                };
            });

        searchContentPages = contentPages;
        if (theme.codeHighlight) {
            await highlightContentEntries(contentPages, theme);
        }

        const homeEntry = contentPages.find((entry) =>
            entry.data.url === "/index.html" || entry.data.url === "/"
        );
        const sidebarPages = docsSidebarPages(contentPages, theme.docsRoot);
        const sidebar = buildSidebar(sidebarPages, theme.docsRoot);
        const flatSidebarPages = flattenSidebar(sidebar);

        if (theme.markdownMirrors) {
            for (
                const entry of contentPages.filter((entry) =>
                    isDocsPath(entry.sourcePath, theme.docsRoot)
                )
            ) {
                allPages.push(Page.create({
                    url: markdownMirrorUrlForPage(entry.data.url),
                    content: renderMarkdownMirror(entry),
                    unlisted: true,
                    search: false,
                }));
            }
        }

        for (const entry of contentPages) {
            entry.page.content = renderDocument({
                page: entry.page,
                body: entry.html,
                sidebar,
                flatGuidePages: flatSidebarPages,
                theme,
            });
        }

        for (const entry of contentPages) {
            const legacyUrl = legacyHtmlUrl(entry.data.url);
            if (!legacyUrl || legacyUrl === entry.data.url) {
                continue;
            }

            allPages.push(Page.create({
                url: legacyUrl,
                content: renderRedirect(entry.data.url),
                unlisted: true,
                search: false,
            }));
        }

        if (theme.llms) {
            allPages.push(Page.create({
                url: "/llms.txt",
                content: renderLlmsIndex(
                    flatSidebarPages,
                    theme,
                    homeEntry?.data,
                ),
                unlisted: true,
                search: false,
            }));

            allPages.push(Page.create({
                url: "/llms-full.txt",
                content: renderLlmsFull(contentPages, flatSidebarPages, theme),
                unlisted: true,
                search: false,
            }));
        }

        for (const [from, to] of Object.entries(theme.redirects)) {
            allPages.push(Page.create({
                url: from,
                content: renderRedirect(to),
                unlisted: true,
                search: false,
            }));
        }

        allPages.push(Page.create({
            url: "/404.html",
            content: renderNotFound(theme),
            unlisted: true,
            search: false,
        }));
    });

    site.process([".html"], (_pages, allPages) => {
        if (!theme.search) {
            return;
        }

        allPages.push(Page.create({
            url: "/search-index.json",
            content: JSON.stringify(
                buildSearchIndex(searchContentPages, theme),
                null,
                2,
            ),
            unlisted: true,
            search: false,
        }));
    });

    site.use(metas());
    site.process([".html"], (pages) => {
        for (const page of pages) {
            addImageDefaults(page);
        }
    });

    if (theme.checkUrls) {
        site.use(checkUrls(theme.checkUrls));
    }

    if (theme.sitemap) {
        site.use(sitemap(theme.sitemap));
    }

    if (theme.basePath !== "/") {
        site.process([".html"], (pages) => {
            for (const page of pages) {
                applyBasePathCanonical(page, theme);
            }
        });
        site.use(basePath());
    }
}

async function highlightContentEntries(
    entries: ContentEntry[],
    theme: ResolvedDocsThemeOptions,
) {
    await highlightCodeBlocks(entries.map((entry) => entry.page), theme);
    for (const entry of entries) {
        applyCodeLineAnnotations(entry.page);
        entry.html = entry.page.document.body.innerHTML;
    }
}

function markdownMirrorUrlForPage(url: string) {
    return legacyHtmlUrl(url)?.replace(/\.html$/, ".md") ??
        `${url.replace(/\/$/, "")}.md`;
}

function readOriginalSource(page: Page): string {
    return page.src.entry
        ? Deno.readTextFileSync(page.src.entry.src)
        : page.text;
}

function markPageAsHtml(page: Page) {
    // Lume's sitemap plugin reads from site.search.pages(), which only returns
    // pages classified as HTML. Extensionless canonical URLs need that preserved.
    Object.defineProperty(page, "isHTML", {
        configurable: true,
        get: () => true,
    });
}
function normalizeLayout(data: DocsData) {
    if (data.layout === undefined) {
        return;
    }

    if (data.layout === false) {
        data.docsLayout = false;
    } else if (["home", "docs", "page"].includes(data.layout)) {
        data.docsLayout = data.layout as "home" | "docs" | "page";
    }
    delete data.layout;
}

function applyLastUpdated(page: Page, theme: ResolvedDocsThemeOptions) {
    if (!theme.lastUpdated) {
        return;
    }

    const data = page.data as DocsData;
    const existing = coerceDate(data.lastmod ?? data.date);
    const date = existing ??
        (theme.lastUpdated.git
            ? gitLastUpdated(sourcePath(page))
            : fileModified(sourcePath(page)));

    if (!date) {
        return;
    }

    data.lastUpdatedDate = date;
    data.lastmod ??= date;
    data.date ??= date;
}

const gitLastUpdatedCache = new Map<string, Date | undefined>();

function gitLastUpdated(path: string): Date | undefined {
    const normalizedPath = path.replace(/^\//, "");
    if (gitLastUpdatedCache.has(normalizedPath)) {
        return gitLastUpdatedCache.get(normalizedPath);
    }
    try {
        const output = new Deno.Command("git", {
            args: ["log", "-1", "--format=%cI", "--", normalizedPath],
        }).outputSync();
        if (!output.success) {
            gitLastUpdatedCache.set(normalizedPath, undefined);
            return undefined;
        }

        const value = new TextDecoder().decode(output.stdout).trim();
        const date = value ? new Date(value) : undefined;
        gitLastUpdatedCache.set(normalizedPath, date);
        return date;
    } catch {
        gitLastUpdatedCache.set(normalizedPath, undefined);
        return undefined;
    }
}

function fileModified(path: string): Date | undefined {
    try {
        return Deno.statSync(path.replace(/^\//, "")).mtime ?? undefined;
    } catch {
        return undefined;
    }
}

function coerceDate(value: Date | string | undefined): Date | undefined {
    if (value instanceof Date) {
        return value;
    }

    if (!value) {
        return undefined;
    }

    const date = new Date(value);
    return Number.isNaN(date.valueOf()) ? undefined : date;
}

function addImageDefaults(page: Page) {
    for (
        const image of page.document.querySelectorAll<HTMLImageElement>("img")
    ) {
        if (!image.hasAttribute("decoding")) {
            image.setAttribute("decoding", "async");
        }

        if (
            !image.hasAttribute("loading") &&
            !image.classList.contains("image-src") &&
            !image.closest(".VPNavBarTitle")
        ) {
            image.setAttribute("loading", "lazy");
        }
    }
}

function applyBasePathCanonical(
    page: Page,
    theme: ResolvedDocsThemeOptions,
) {
    if (!theme.location) {
        return;
    }

    const data = page.data as DocsData;
    if (data.canonical === false || typeof data.canonical === "string") {
        return;
    }

    const canonical = page.document.querySelector<HTMLLinkElement>(
        'link[rel="canonical"]',
    );
    if (!canonical) {
        return;
    }

    const base = theme.basePath.replace(/\/$/, "");
    canonical.setAttribute(
        "href",
        new URL(`${base}${data.url}`, theme.location).href,
    );
}

function renderRedirect(target: string) {
    const safeTarget = target.replace(/"/g, "%22");
    const jsTarget = JSON.stringify(safeTarget);
    return `<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=${safeTarget}"><link rel="canonical" href="${safeTarget}"><title>Redirecting...</title><script>const target=${jsTarget};window.location.replace(target+(target.includes("#")?"":window.location.hash));</script></head><body><p>Redirecting to <a href="${safeTarget}">${safeTarget}</a>.</p></body></html>`;
}

function resolveOptions(options: DocsThemeOptions): ResolvedDocsThemeOptions {
    const docsRoot = normalizeDocsRoot(options.docsRoot ?? "guide");
    const label = options.brand?.label ?? options.title;
    const location = options.location
        ? typeof options.location === "string"
            ? new URL(options.location)
            : options.location
        : undefined;
    const base = normalizeBasePath(
        options.basePath ?? location?.pathname ?? "/",
    );
    const codeHighlightOptions = options.codeHighlight === false
        ? false
        : options.codeHighlight ?? {};
    const lastUpdated = options.lastUpdated === undefined ||
            options.lastUpdated === false
        ? false
        : {
            git: typeof options.lastUpdated === "object"
                ? options.lastUpdated.git ?? false
                : false,
            text: typeof options.lastUpdated === "object"
                ? options.lastUpdated.text ?? defaultLabels.lastUpdated
                : defaultLabels.lastUpdated,
        };
    const defaultSitemap = lastUpdated
        ? { items: { lastmod: "=lastmod" } }
        : { items: { lastmod: "__docsLastmodDisabled" } };

    return {
        title: options.title,
        description: options.description,
        location,
        basePath: base,
        docsRoot,
        head: options.head ?? [],
        metas: {
            robots: true,
            ...(options.metas ?? {}),
        },
        labels: {
            ...defaultLabels,
            ...(options.labels ?? {}),
        },
        brand: {
            label,
            logoLight: options.brand?.logoLight ?? options.brand?.icon ?? "",
            logoDark: options.brand?.logoDark ?? options.brand?.logoLight ??
                options.brand?.icon ?? "",
            icon: options.brand?.icon ?? options.brand?.logoLight ?? "",
            themeColor: options.brand?.themeColor ?? "#3c8772",
        },
        nav: options.nav ?? [
            { text: "Guide", link: docsUrlPrefix(docsRoot).replace(/\/$/, "") },
        ],
        social: options.social ?? [],
        footer: {
            message: options.footer?.message ??
                "Released under the MIT License.",
            copyright: options.footer?.copyright ?? "",
        },
        editLink: options.editLink
            ? {
                text: options.editLink.text ?? defaultLabels.editLink,
                pattern: options.editLink.pattern,
            }
            : undefined,
        lastUpdated,
        outline: {
            level: options.outline?.level ?? 2,
            label: options.outline?.label ?? defaultLabels.outline,
        },
        sitemap: options.sitemap === false ? false : {
            ...defaultSitemap,
            ...(options.sitemap === true || options.sitemap === undefined
                ? {}
                : options.sitemap),
        },
        checkUrls: options.checkUrls === false ? false : {
            anchors: true,
            throw: true,
            ...(options.checkUrls === true || options.checkUrls === undefined
                ? {}
                : options.checkUrls),
        },
        codeHighlight: codeHighlightOptions === false ? false : {
            themes: {
                light: codeHighlightOptions.themes?.light ?? "github-light",
                dark: codeHighlightOptions.themes?.dark ?? "github-dark",
            },
            langs: codeHighlightOptions.langs ?? defaultCodeHighlightLanguages,
        },
        code: {
            lineNumbers: options.code?.lineNumbers ?? false,
        },
        snippets: options.snippets === false ? false : {
            docsRoot: options.snippets?.docsRoot ?? ".",
            repoRoot: options.snippets?.repoRoot ?? "..",
            aliases: options.snippets?.aliases ?? {},
        },
        redirects: options.redirects ?? {},
        llms: options.llms ?? true,
        markdownMirrors: options.markdownMirrors ?? true,
        search: options.search ?? true,
        assets: options.assets ?? true,
        rawPagesDir: options.rawPagesDir === undefined
            ? "pages"
            : options.rawPagesDir,
        ignore: [
            ".build",
            ".npmrc",
            ".vitepress",
            "_lume",
            "_site",
            "_theme",
            "node_modules",
            "package-lock.json",
            "package.json",
            "README.md",
            ...(options.ignore ?? []),
        ],
    };
}

function normalizeBasePath(path: string) {
    const normalized = `/${path}`.replace(/\/+/g, "/").replace(/\/$/, "");
    return normalized === "" ? "/" : `${normalized}/`.replace(/\/\/+/g, "/");
}

function directoryExists(path: string): boolean {
    try {
        return Deno.statSync(path).isDirectory;
    } catch (error) {
        if (error instanceof Deno.errors.NotFound) {
            return false;
        }
        throw error;
    }
}

const defaultCodeHighlightLanguages = [
    "csharp",
    "shellscript",
    "powershell",
    "json",
    "xml",
    "html",
    "css",
    "javascript",
    "typescript",
    "yaml",
    "markdown",
    "text",
];
const defaultLabels: ThemeLabels = {
    search: "Search",
    searchPlaceholder: "Search",
    searchNoResults: "No results found.",
    searchRecent: "Recent searches",
    searchClose: "Close search",
    menu: "Menu",
    returnTop: "Return to top",
    outline: "On this page",
    editLink: "Edit this page",
    lastUpdated: "Last updated",
    previousPage: "Previous page",
    nextPage: "Next page",
    notFoundTitle: "Page Not Found",
    notFoundMessage: "The page you are looking for does not exist.",
    notFoundLink: "Return home",
};
