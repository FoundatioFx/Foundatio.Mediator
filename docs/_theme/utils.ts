import type { Badge, DocsData, Heading } from "./types.ts";
import type { Page } from "lume/core/file.ts";

export function sourcePath(page: Page): string {
    return page.sourcePath.replaceAll("\\", "/");
}

export function normalizeDocsRoot(root: string): string {
    return root.replace(/^\/+|\/+$/g, "") || "guide";
}

export function docsUrlPrefix(root: string): string {
    return `/${normalizeDocsRoot(root)}/`;
}

export function isDocsPath(path: string, root: string): boolean {
    return path.startsWith(docsUrlPrefix(root));
}

export function isMarkdownPage(page: Page): boolean {
    const data = page.data as DocsData;
    const path = sourcePath(page);
    return page.isHTML &&
        path.endsWith(".md") &&
        !path.startsWith("/_") &&
        Boolean(data.url) &&
        data.theme !== false &&
        data.docsLayout !== false;
}

export function pageLayout(data: DocsData, path: string, docsRoot: string) {
    if (data.docsLayout !== undefined) {
        return data.docsLayout;
    }

    if (data.theme !== undefined) {
        return data.theme;
    }

    if (data.layout === "home") {
        return "home";
    }

    return isDocsPath(path, docsRoot) ? "docs" : "page";
}

export function normalizeInternalLinks(
    html: string,
    currentUrl?: string,
): string {
    return html.replace(
        /\shref="([^"]+)"/g,
        (_match, href: string) =>
            ` href="${escapeAttribute(normalizeHref(href, currentUrl))}"`,
    );
}

export function normalizeHref(href: string, currentUrl?: string): string {
    if (
        href.startsWith("#") ||
        /^[a-z][a-z0-9+.-]*:/i.test(href) ||
        href.startsWith("//")
    ) {
        return href;
    }

    const [path, hash = ""] = href.split("#", 2);
    const suffix = hash ? `#${hash}` : "";

    if (
        path.endsWith(".txt") ||
        path.endsWith(".json") ||
        path === ""
    ) {
        return `${path}${suffix}`;
    }

    const normalizedPath = prettyPagePath(path);
    if (!currentUrl || path.startsWith("/")) {
        return `${normalizedPath}${suffix}`;
    }

    const resolved = new URL(
        normalizedPath,
        `https://docs.local${pageDirectoryUrl(currentUrl)}`,
    );
    return `${resolved.pathname}${suffix}`;
}

function pageDirectoryUrl(currentUrl: string): string {
    let path = currentUrl.split("#", 1)[0] || "/";
    if (path === "/") {
        return path;
    }

    if (path.endsWith("/")) {
        path = path.slice(0, -1);
    }

    const index = path.lastIndexOf("/");
    return index <= 0 ? "/" : `${path.slice(0, index + 1)}`;
}

export function cleanPageUrl(url: string): string {
    return normalizeHref(url);
}

export function markdownMirrorUrl(url: string): string {
    const path = url.split("#", 1)[0];
    if (path === "" || path === "/" || path === "/index.html") {
        return "/index.md";
    }

    let normalized = path;
    if (normalized.endsWith("/")) {
        normalized = normalized.slice(0, -1);
    }

    normalized = normalized
        .replace(/\/index\.html$/, "")
        .replace(/\.html$/, "");

    return `${normalized || "/index"}.md`;
}

export function legacyHtmlUrl(url: string): string | undefined {
    const path = url.split("#", 1)[0];
    if (
        path === "" ||
        path === "/" ||
        path.endsWith(".html") ||
        path.endsWith(".txt") ||
        path.endsWith(".json")
    ) {
        return undefined;
    }

    const normalized = path.endsWith("/") ? path.slice(0, -1) : path;
    if (!normalized || normalized === "/index") {
        return undefined;
    }

    return `${normalized}.html`;
}

function prettyPagePath(path: string): string {
    if (path === "") {
        return path;
    }

    if (path === "/" || path.endsWith("/")) {
        return path;
    }

    if (path === "index.md" || path === "index.html") {
        return ".";
    }

    if (path.endsWith("/index.md") || path.endsWith("/index.html")) {
        return withTrailingSlash(
            path.replace(/\/?index\.(?:md|html)$/, "") || "/",
        );
    }

    if (path.endsWith(".md") || path.endsWith(".html")) {
        return withTrailingSlash(path.replace(/\.(?:md|html)$/, ""));
    }

    if (path.split("/").pop()?.includes(".")) {
        return path;
    }

    return withTrailingSlash(path);
}

function withTrailingSlash(path: string): string {
    return path === "/" || path.endsWith("/") ? path : `${path}/`;
}

export function firstHeading(data: DocsData): string | undefined {
    return data.headings?.find((heading) => heading.level === 1)?.text;
}

export function titleFromPath(path: string): string {
    const name = path.split("/").pop()?.replace(/\.md$/, "") ?? path;
    return name
        .split("-")
        .map((part) =>
            part ? part.charAt(0).toUpperCase() + part.slice(1) : part
        )
        .join(" ");
}

export function extractHeadings(html: string): Heading[] {
    const headings: Heading[] = [];
    const pattern = /<h([1-6])\b[^>]*\bid="([^"]+)"[^>]*>([\s\S]*?)<\/h\1>/g;
    for (const match of html.matchAll(pattern)) {
        headings.push({
            level: Number(match[1]),
            slug: decodeHtml(match[2]),
            text: parseHeadingAnchor(htmlToText(match[3])).text,
        });
    }

    return headings;
}

export function transformBadgeTags(html: string): string {
    return html
        .replace(/<Badge\s+([^>]*?)\s*\/>/g, (match, attrs: string) => {
            const text = badgeAttribute(attrs, "text");
            if (!text) {
                return match;
            }

            return renderBadge({
                text: decodeHtml(text),
                type: badgeAttribute(attrs, "type") ?? undefined,
            });
        })
        .replace(
            /<Badge\s*([^>]*)>([\s\S]*?)<\/Badge>/g,
            (_match, attrs: string, text: string) =>
                renderBadge({
                    text: htmlToText(text),
                    type: badgeAttribute(attrs, "type") ?? undefined,
                }),
        );
}

export function renderBadge(value: string | Badge | undefined): string {
    if (!value) {
        return "";
    }

    const badge = typeof value === "string" ? { text: value } : value;
    const type = badge.type ?? "info";
    return `<span class="VPBadge ${escapeAttribute(type)}">${
        escapeHtml(badge.text)
    }</span>`;
}

function badgeAttribute(attrs: string, name: string): string | undefined {
    const pattern = new RegExp(`${name}=["']([^"']+)["']`, "i");
    return pattern.exec(attrs)?.[1];
}

export function htmlToText(html: string): string {
    return html
        .replace(/<pre class="mermaid">([\s\S]*?)<\/pre>/g, "$1")
        .replace(/<code[^>]*>([\s\S]*?)<\/code>/g, "$1")
        .replace(/<[^>]+>/g, " ")
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&amp;/g, "&")
        .replace(/&quot;/g, '"')
        .replace(/&#39;/g, "'")
        .replace(/[ \t]+/g, " ")
        .replace(/\n\s+/g, "\n")
        .trim();
}

export function uniqueSlug(
    baseSlug: string,
    headings: Array<{ slug: string }>,
): string {
    const base = baseSlug || "section";
    let slug = base;
    let index = 1;
    const used = new Set(headings.map((heading) => heading.slug));

    while (used.has(slug)) {
        slug = `${base}-${index++}`;
    }

    return slug;
}

const controlCharacters = /[\u0000-\u001f]/g;
const specialCharacters = /[\s~`!@#$%^&*()\-_+=[\]{}|\\;:"'‘’“”<>,.?/]+/g;
const customHeadingAnchorPattern = /\s*\{#([A-Za-z0-9][\w-]*)\}\s*$/;

export function parseHeadingAnchor(value: string): {
    text: string;
    id?: string;
} {
    const match = customHeadingAnchorPattern.exec(value);
    if (!match) {
        return { text: value };
    }

    return {
        text: value.slice(0, match.index).trimEnd(),
        id: match[1],
    };
}

export function slugify(value: string): string {
    return decodeHtml(value)
        .normalize("NFKD")
        .replace(controlCharacters, "")
        .replace(/</g, "-lt-")
        .replace(/>/g, "-gt-")
        .replace(specialCharacters, "-")
        .replace(/-{2,}/g, "-")
        .replace(/^-+|-+$/g, "")
        .replace(/^(\d)/, "_$1")
        .toLowerCase();
}

export function slugAliases(slug: string): string[] {
    const aliases = new Set<string>();
    const dashless = slug.replace(/-(?:—|–)-/g, "-");
    aliases.add(dashless);
    aliases.add(encodeURIComponent(slug));
    aliases.add(encodeURIComponent(dashless));

    return [...aliases].filter((alias) => alias && alias !== slug);
}

export function decodeHtml(value: string): string {
    return value
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&amp;/g, "&")
        .replace(/&quot;/g, '"')
        .replace(/&#39;/g, "'")
        .replace(/&#96;/g, "`");
}

export function escapeHtml(value: string): string {
    return value
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

export function escapeAttribute(value: string): string {
    return escapeHtml(value).replace(/`/g, "&#96;");
}
