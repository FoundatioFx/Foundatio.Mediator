import type { DocsData, SidebarPage, SidebarSection } from "./types.ts";
import {
    docsUrlPrefix,
    firstHeading,
    isDocsPath,
    sourcePath,
    titleFromPath,
} from "./utils.ts";
import type { Page } from "lume/core/file.ts";

export function toSidebarPage(page: Page, docsRoot: string): SidebarPage {
    const data = page.data as DocsData;
    const meta = data.nav ?? {};
    const title = meta.title ?? data.title ?? firstHeading(data) ??
        titleFromPath(sourcePath(page));

    return {
        title,
        url: data.url,
        sourcePath: sourcePath(page),
        section: meta.section ?? sectionFromPath(sourcePath(page), docsRoot),
        sectionOrder: meta.sectionOrder ?? 100,
        order: meta.order ?? 100,
        level: 1,
        collapsed: meta.collapsed ?? false,
        badge: typeof meta.badge === "string"
            ? { text: meta.badge }
            : meta.badge,
        children: [],
    };
}

export function buildSidebar(
    pages: SidebarPage[],
    docsRoot: string,
): SidebarSection[] {
    const sections = new Map<string, SidebarSection>();

    for (const page of pages) {
        const section = sections.get(page.section) ?? {
            title: page.section,
            order: page.sectionOrder,
            items: [],
        };
        section.order = Math.min(section.order, page.sectionOrder);
        insertPage(section.items, page, docsRoot);
        sections.set(page.section, section);
    }

    return [...sections.values()]
        .sort((left, right) =>
            left.order - right.order || left.title.localeCompare(right.title)
        )
        .map((section) => ({
            ...section,
            items: sortItems(assignLevels(section.items, 1)),
        }));
}

export function docsSidebarPages(
    pages: Array<{ page: Page; sourcePath: string }>,
    docsRoot: string,
) {
    return pages
        .filter((entry) => isDocsPath(entry.sourcePath, docsRoot))
        .filter((entry) => (entry.page.data as DocsData).nav?.hidden !== true)
        .map((entry) => toSidebarPage(entry.page, docsRoot));
}

export function flattenSidebar(sections: SidebarSection[]): SidebarPage[] {
    return sections.flatMap((section) => flattenItems(section.items));
}

function insertPage(items: SidebarPage[], page: SidebarPage, docsRoot: string) {
    const parts = relativeDocsParts(page.sourcePath ?? "", docsRoot);
    if (parts.length <= 1) {
        items.push(page);
        return;
    }

    let current = items;
    for (const folder of parts.slice(0, -1)) {
        let group = current.find((item) =>
            !item.url && item.title === titleFromPath(folder)
        );
        if (!group) {
            group = {
                title: titleFromPath(folder),
                section: page.section,
                sectionOrder: page.sectionOrder,
                order: 100,
                level: 1,
                collapsed: false,
                children: [],
            };
            current.push(group);
        }
        current = group.children;
    }

    current.push(page);
}

function relativeDocsParts(path: string, docsRoot: string) {
    const prefix = docsUrlPrefix(docsRoot);
    const relative = path.startsWith(prefix) ? path.slice(prefix.length) : path;
    return relative.replace(/\.md$/, "").split("/").filter(Boolean);
}

function assignLevels(items: SidebarPage[], level: number): SidebarPage[] {
    return items.map((item) => ({
        ...item,
        level,
        children: assignLevels(item.children, level + 1),
    }));
}

function sortItems(items: SidebarPage[]): SidebarPage[] {
    return items
        .sort((left, right) =>
            left.order - right.order || left.title.localeCompare(right.title)
        )
        .map((item) => ({
            ...item,
            children: sortItems(item.children),
        }));
}

function flattenItems(items: SidebarPage[]): SidebarPage[] {
    return items.flatMap((item) => [
        ...(item.url ? [item] : []),
        ...flattenItems(item.children),
    ]);
}

function sectionFromPath(path: string, docsRoot: string): string {
    if (isDocsPath(path, docsRoot)) {
        return titleFromPath(docsRoot);
    }

    const parts = path.split("/").filter(Boolean);
    return parts.length > 1 ? titleFromPath(parts[0]) : "Guide";
}
