import type {
    Action,
    ContentEntry,
    DocsData,
    Feature,
    HeadAttrs,
    HeadEntry,
    Heading,
    HomeSection,
    NavLink,
    OutlineLevel,
    PageLink,
    ResolvedDocsThemeOptions,
    SidebarPage,
    SidebarSection,
    SocialLink,
} from "./types.ts";
import {
    decodeHtml,
    escapeAttribute,
    escapeHtml,
    firstHeading,
    htmlToText,
    markdownMirrorUrl,
    normalizeHref,
    pageLayout,
    renderBadge,
    sourcePath,
    titleFromPath,
} from "./utils.ts";
import type { Page } from "lume/core/file.ts";

export function renderDocument(options: {
    page: Page;
    body: string;
    sidebar: SidebarSection[];
    flatGuidePages: SidebarPage[];
    theme: ResolvedDocsThemeOptions;
}) {
    const data = options.page.data as DocsData;
    const path = sourcePath(options.page);
    const layout = pageLayout(data, path, options.theme.docsRoot);
    const title = documentTitle(data, options.theme, layout === "home");
    const description = data.description ?? options.theme.description;
    prepareMetas(data, title, description, options.theme);

    const chrome = pageChrome(data, layout);
    const hasSidebar = layout === "docs" && chrome.sidebar;
    const hasAside = layout === "docs" && chrome.aside &&
        resolveOutline(data, options.theme).enabled;
    const body = layout === "home"
        ? renderHome(
            data,
            replaceToc(options.body, data, options.theme),
            options.theme,
        )
        : layout === "docs"
        ? renderDocsPage(
            options.page,
            replaceToc(options.body, data, options.theme),
            options.flatGuidePages,
            options.theme,
            hasAside,
            chrome,
        )
        : renderContentPage(replaceToc(options.body, data, options.theme));

    return renderShell({
        title,
        description,
        data,
        hasSidebar,
        isHome: layout === "home",
        body,
        sidebar: hasSidebar ? renderSidebar(options.sidebar, data.url) : "",
        theme: options.theme,
        showFooter: layout === "home" && chrome.footer,
    });
}

function documentTitle(
    data: DocsData,
    theme: ResolvedDocsThemeOptions,
    isHome: boolean,
) {
    const title = firstHeading(data) ?? data.title ?? theme.title;
    if (isHome || title === theme.title) {
        return theme.title;
    }

    return `${title} | ${theme.title}`;
}

export function renderNotFound(theme: ResolvedDocsThemeOptions) {
    return renderShell({
        title: `${theme.labels.notFoundTitle} | ${theme.title}`,
        description: theme.description,
        data: {
            url: "/404.html",
            title: theme.labels.notFoundTitle,
            head: [renderTrailingSlashRedirect(theme)],
        },
        hasSidebar: false,
        isHome: false,
        theme,
        showFooter: false,
        body: `<main class="not-found"><h1>${
            escapeHtml(theme.labels.notFoundTitle)
        }</h1><p>${
            escapeHtml(theme.labels.notFoundMessage)
        }</p><p><a class="VPButton medium brand" href="/">${
            escapeHtml(theme.labels.notFoundLink)
        }</a></p></main>`,
    });
}

function renderTrailingSlashRedirect(theme: ResolvedDocsThemeOptions) {
    const basePath = theme.basePath;
    return `<script>(()=>{const path=window.location.pathname;const base=${
        JSON.stringify(basePath)
    };const root=base==="/"?"/":base.endsWith("/")?base.slice(0,-1):base;if(path.length>1&&path.endsWith("/")&&path!==base&&path!==root+"/"){window.location.replace(path.slice(0,-1)+window.location.search+window.location.hash);}})();</script>`;
}

function renderShell(options: {
    title: string;
    description: string;
    data: DocsData;
    hasSidebar: boolean;
    isHome: boolean;
    body: string;
    theme: ResolvedDocsThemeOptions;
    showFooter: boolean;
    sidebar?: string;
}) {
    const navClass = [
        "VPNavBar",
        options.isHome ? "home" : "",
        options.isHome ? "top" : "",
        options.hasSidebar ? "has-sidebar" : "",
    ].filter(Boolean).join(" ");
    const contentClass = [
        "VPContent",
        options.isHome ? "is-home" : "",
        options.hasSidebar ? "has-sidebar" : "",
        options.data.pageClass ?? "",
    ].filter(Boolean).join(" ");
    const footerClass = ["VPFooter", options.hasSidebar ? "has-sidebar" : ""]
        .filter(Boolean).join(" ");
    const { theme } = options;
    const head = renderHeadEntries([
        ...coreMetaHead(options),
        ...canonicalHead(options.data, theme),
        ...theme.head,
        ...(options.data.head ?? []),
    ]);
    const lang = options.data.lang ?? "en-US";

    return `<!doctype html>
<html lang="${escapeAttribute(lang)}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>${escapeHtml(options.title)}</title>
  <link rel="icon" href="${escapeAttribute(theme.brand.icon)}" type="image/png">
  <link rel="preload" href="/assets/inter-roman-latin.woff2" as="font" type="font/woff2" crossorigin>
  <link rel="stylesheet" href="/assets/site.css">
  ${head}
  <script>window.__LUME_DOCS_BASE_PATH__=${
        JSON.stringify(theme.basePath)
    };window.__LUME_DOCS_LABELS__=${JSON.stringify(theme.labels)};</script>
  <script id="check-dark-mode">(() => { const preference = localStorage.getItem("lume-docs-theme") || "auto"; const systemDark = window.matchMedia("(prefers-color-scheme: dark)").matches; if ((!preference || preference === "auto" ? systemDark : preference === "dark")) document.documentElement.classList.add("dark"); })();</script>
</head>
<body>
  <div id="app"><div class="Layout">
    <a href="#VPContent" class="VPSkipLink visually-hidden">Skip to content</a>
    ${
        options.data.navbar === false ? "" : `<header class="VPNav">
      <div class="${navClass}">
        <div class="wrapper">
          <div class="container">
            <div class="title">
              <a class="VPNavBarTitle" href="/">
                <img class="VPImage dark logo" src="${
            escapeAttribute(theme.brand.logoDark)
        }" alt="">
                <img class="VPImage light logo" src="${
            escapeAttribute(theme.brand.logoLight)
        }" alt="">
                <span>${escapeHtml(theme.brand.label)}</span>
              </a>
            </div>
            <div class="content">
              <div class="content-body">
                ${theme.search ? renderSearchButton(theme) : ""}
                ${renderMainNav(theme.nav, options.data.url)}
                <button class="VPSwitch VPSwitchAppearance" type="button" role="switch" aria-label="Toggle dark mode"><span class="check"><span class="icon"><span class="vpi-sun sun"></span><span class="vpi-moon moon"></span></span></span></button>
                ${renderSocialLinks(theme.social)}
                <button type="button" class="VPNavBarHamburger hamburger" aria-label="mobile navigation" aria-expanded="false"><span class="container"><span class="top"></span><span class="middle"></span><span class="bottom"></span></span></button>
              </div>
            </div>
          </div>
        </div>
        <div class="divider"><div class="divider-line"></div></div>
      </div>
      ${renderMobileNavScreen(theme, options.data.url)}
    </header>`
    }
    ${options.hasSidebar ? renderLocalNav(theme, options.data) : ""}
    ${options.sidebar ?? ""}
    <div class="${contentClass}" id="VPContent">${options.body}</div>
    ${
        options.showFooter
            ? `<footer class="${footerClass}"><div class="container"><p class="message">${
                escapeHtml(theme.footer.message)
            }</p><p class="copyright">${
                escapeHtml(theme.footer.copyright)
            }</p></div></footer>`
            : ""
    }
  </div></div>
  <script type="module" src="/assets/site.js"></script>
</body>
</html>`;
}

function renderSearchButton(theme: ResolvedDocsThemeOptions) {
    return `<div class="VPNavBarSearch search"><button class="VPNavBarSearchButton" type="button" aria-label="${
        escapeAttribute(theme.labels.search)
    }" aria-keyshortcuts="/ control+k meta+k">
      <span class="vpi-search" aria-hidden="true"></span><span class="text">${
        escapeHtml(theme.labels.search)
    }</span>
      <span class="keys" aria-hidden="true"><kbd class="key-cmd">&#8984;</kbd><kbd class="key-ctrl">Ctrl</kbd><kbd>K</kbd></span>
    </button></div>`;
}

function renderMainNav(nav: NavLink[], currentUrl: string) {
    if (nav.length === 0) {
        return "";
    }

    return `<nav class="VPNavBarMenu menu" aria-label="Main Navigation">${
        nav.map((item) => renderMainNavItem(item, currentUrl)).join("")
    }</nav>`;
}

function renderMainNavItem(item: NavLink, currentUrl: string): string {
    if (item.items?.length) {
        return `<div class="VPNavBarMenuGroup${
            isNavActive(item, currentUrl) ? " active" : ""
        }"><button type="button" class="button"><span>${
            escapeHtml(item.text)
        }</span><span class="chevron"></span></button><div class="items">${
            item.items.map((child) => renderMainNavItem(child, currentUrl))
                .join("")
        }</div></div>`;
    }

    if (!item.link) {
        return `<span class="VPNavBarMenuLink">${escapeHtml(item.text)}</span>`;
    }

    const external = item.external ?? /^[a-z][a-z0-9+.-]*:/i.test(item.link);
    const rel = item.rel ?? (external ? "noreferrer" : undefined);
    const target = item.target ?? (external ? "_blank" : undefined);
    const classes = [
        "VPNavBarMenuLink",
        isNavActive(item, currentUrl) ? "active" : "",
        external && !item.noIcon ? "vp-external-link-icon" : "",
    ].filter(Boolean).join(" ");

    return `<a class="${classes}" href="${
        escapeAttribute(normalizeHref(item.link))
    }"${target ? ` target="${escapeAttribute(target)}"` : ""}${
        rel ? ` rel="${escapeAttribute(rel)}"` : ""
    }>${escapeHtml(item.text)}</a>`;
}

function isNavActive(item: NavLink, currentUrl: string): boolean {
    if (item.activeMatch && new RegExp(item.activeMatch).test(currentUrl)) {
        return true;
    }

    if (item.link && normalizeHref(item.link) === currentUrl) {
        return true;
    }

    return item.items?.some((child) => isNavActive(child, currentUrl)) ?? false;
}

function renderMobileNavScreen(
    theme: ResolvedDocsThemeOptions,
    currentUrl: string,
) {
    return `<div class="VPNavScreen"><div class="container"><nav class="VPNavScreenMenu menu" aria-label="Mobile Navigation">${
        theme.nav.map((item) => renderMobileNavItem(item, currentUrl)).join("")
    }</nav><div class="VPNavScreenAppearance appearance"><p class="text">Appearance</p><button class="VPSwitch VPSwitchAppearance" type="button" role="switch" aria-label="Toggle dark mode"><span class="check"><span class="icon"><span class="vpi-sun sun"></span><span class="vpi-moon moon"></span></span></span></button></div>${
        renderSocialLinks(theme.social, "VPNavScreenSocialLinks social-links")
    }</div></div>`;
}

function renderMobileNavItem(item: NavLink, currentUrl: string): string {
    if (item.items?.length) {
        return `<div class="VPNavScreenMenuGroup${
            isNavActive(item, currentUrl) ? " active" : ""
        }"><button type="button" class="button"><span>${
            escapeHtml(item.text)
        }</span><span class="chevron"></span></button><div class="items">${
            item.items.map((child) => renderMobileNavItem(child, currentUrl))
                .join("")
        }</div></div>`;
    }

    if (!item.link) {
        return `<span class="VPNavScreenMenuLink">${
            escapeHtml(item.text)
        }</span>`;
    }

    const external = item.external ?? /^[a-z][a-z0-9+.-]*:/i.test(item.link);
    const rel = item.rel ?? (external ? "noreferrer" : undefined);
    const target = item.target ?? (external ? "_blank" : undefined);
    const classes = [
        "VPLink",
        "link",
        "VPNavScreenMenuLink",
        isNavActive(item, currentUrl) ? "active" : "",
        external && !item.noIcon ? "vp-external-link-icon" : "",
    ].filter(Boolean).join(" ");

    return `<a class="${classes}" href="${
        escapeAttribute(normalizeHref(item.link))
    }"${target ? ` target="${escapeAttribute(target)}"` : ""}${
        rel ? ` rel="${escapeAttribute(rel)}"` : ""
    }>${escapeHtml(item.text)}</a>`;
}

function renderSocialLinks(links: SocialLink[], className = "") {
    if (links.length === 0) {
        return "";
    }

    const classes = ["VPSocialLinks", className].filter(Boolean).join(" ");

    return `<div class="${escapeAttribute(classes)}">${
        links.map((link) => {
            const className = link.class ??
                link.label.toLowerCase().replace(/[^a-z0-9_-]/g, "-");
            const icon = link.icon ?? builtInSocialIcon(className);
            return `<a class="VPSocialLink ${
                escapeAttribute(className)
            }" href="${escapeAttribute(link.link)}" aria-label="${
                escapeAttribute(link.label)
            }" target="_blank" rel="me noopener">${
                icon ? `<span class="icon">${icon}</span>` : ""
            }${link.text ? `<span>${escapeHtml(link.text)}</span>` : ""}</a>`;
        }).join("")
    }</div>`;
}

function builtInSocialIcon(name: string): string {
    switch (name.toLowerCase()) {
        case "github":
            return '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 .5a12 12 0 0 0-3.79 23.39c.6.11.82-.26.82-.58v-2.04c-3.34.73-4.04-1.61-4.04-1.61-.55-1.39-1.34-1.76-1.34-1.76-1.09-.75.08-.74.08-.74 1.21.09 1.85 1.24 1.85 1.24 1.07 1.84 2.81 1.31 3.5 1 .11-.78.42-1.31.76-1.61-2.67-.3-5.47-1.33-5.47-5.93 0-1.31.47-2.38 1.24-3.22-.12-.3-.54-1.52.12-3.18 0 0 1.01-.32 3.3 1.23a11.5 11.5 0 0 1 6.01 0c2.29-1.55 3.3-1.23 3.3-1.23.66 1.66.24 2.88.12 3.18.77.84 1.24 1.91 1.24 3.22 0 4.61-2.81 5.63-5.49 5.93.43.37.82 1.1.82 2.22v3.29c0 .32.22.7.83.58A12 12 0 0 0 12 .5Z"/></svg>';
        case "discord":
            return '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20.32 4.37A19.8 19.8 0 0 0 15.36 2.8a13.7 13.7 0 0 0-.63 1.3 18.5 18.5 0 0 0-5.46 0 13.7 13.7 0 0 0-.64-1.3 19.7 19.7 0 0 0-4.95 1.57C.55 9.05-.3 13.61.12 18.1a19.9 19.9 0 0 0 6.07 3.08 14.8 14.8 0 0 0 1.3-2.1 12.9 12.9 0 0 1-2.05-.98l.5-.39a14.2 14.2 0 0 0 12.12 0l.5.39c-.65.39-1.34.72-2.06.98.38.74.82 1.44 1.3 2.1a19.9 19.9 0 0 0 6.08-3.08c.5-5.2-.84-9.72-3.56-13.73ZM8.02 15.33c-1.18 0-2.16-1.08-2.16-2.41s.96-2.42 2.16-2.42c1.2 0 2.18 1.1 2.16 2.42 0 1.33-.96 2.41-2.16 2.41Zm7.96 0c-1.18 0-2.16-1.08-2.16-2.41s.96-2.42 2.16-2.42c1.2 0 2.18 1.1 2.16 2.42 0 1.33-.96 2.41-2.16 2.41Z"/></svg>';
        default:
            return "";
    }
}

function renderLocalNav(theme: ResolvedDocsThemeOptions, data: DocsData) {
    const outline = resolveOutline(data, theme);
    const headings = outline.enabled
        ? (data.headings ?? []).filter((heading) =>
            heading.level >= outline.min && heading.level <= outline.max
        )
        : [];
    const outlineNav = headings.length > 0
        ? `<details class="VPLocalNavOutline"><summary>${
            escapeHtml(outline.label)
        }</summary><div class="items">${
            headings.map((heading) =>
                `<a class="level-${heading.level}" href="#${
                    escapeAttribute(heading.slug)
                }">${escapeHtml(heading.text)}</a>`
            ).join("")
        }</div></details>`
        : `<button class="return-top" type="button">${
            escapeHtml(theme.labels.returnTop)
        }</button>`;

    const localNavClass = `VPLocalNav has-sidebar${
        headings.length === 0 ? " empty" : ""
    }`;

    return `<div class="${localNavClass}"><div class="container"><button class="menu" aria-expanded="false" aria-controls="VPSidebarNav"><span class="menu-icon"></span><span class="menu-text">${
        escapeHtml(theme.labels.menu)
    }</span></button>${outlineNav}</div></div>`;
}
function renderHome(
    data: DocsData,
    content: string,
    theme: ResolvedDocsThemeOptions,
): string {
    const hero = data.hero ?? {};
    const features = data.features ?? [];
    const sections = data.homeSections ?? [];

    return `<div class="VPHome">
    <div class="VPHero has-image VPHomeHero">
      <div class="container">
        <div class="main">
          <h1 class="heading"><span class="name clip">${
        escapeHtml(hero.name ?? theme.title)
    }</span><span class="text">${escapeHtml(hero.text ?? "")}</span></h1>
          <p class="tagline">${escapeHtml(hero.tagline ?? "")}</p>
          <div class="actions">${
        (hero.actions ?? []).map(renderAction).join("")
    }</div>
        </div>
        <div class="image"><div class="image-container"><div class="image-bg"></div><img class="VPImage image-src" src="${
        escapeAttribute(hero.image?.src ?? theme.brand.icon)
    }" alt="${escapeAttribute(hero.image?.alt ?? theme.title)}"></div></div>
      </div>
    </div>
    <div class="VPFeatures VPHomeFeatures"><div class="container"><div class="items">${
        features.map(renderFeature).join("")
    }</div></div></div>
    ${renderHomeSections(sections)}
    <div class="vp-doc container">${content}</div>
  </div>`;
}

function renderHomeSections(sections: HomeSection[]): string {
    if (sections.length === 0) {
        return "";
    }

    return sections.map((section) =>
        `<section class="VPHomeSection"><div class="container">${
            section.title ? `<h2>${escapeHtml(section.title)}</h2>` : ""
        }${
            section.text
                ? `<p class="text">${escapeHtml(section.text)}</p>`
                : ""
        }${
            section.links?.length
                ? `<div class="actions">${
                    section.links.map(renderAction).join("")
                }</div>`
                : ""
        }${
            section.features?.length
                ? `<div class="items">${
                    section.features.map(renderFeature).join("")
                }</div>`
                : ""
        }</div></section>`
    ).join("");
}
function renderAction(action: Action): string {
    const theme = action.theme === "alt" ? "alt" : "brand";
    return `<div class="action"><a class="VPButton medium ${theme}" href="${
        escapeAttribute(normalizeHref(action.link))
    }">${escapeHtml(action.text)}</a></div>`;
}

function renderFeature(feature: Feature): string {
    const box = `<article class="box">${
        feature.icon ? `<div class="icon">${feature.icon}</div>` : ""
    }<h2 class="title">${escapeHtml(feature.title)}</h2><p class="details">${
        escapeHtml(feature.details)
    }</p></article>`;
    return `<div class="grid-6 item">${
        feature.link
            ? `<a class="VPFeature" href="${
                escapeAttribute(normalizeHref(feature.link))
            }">${box}${
                feature.linkText
                    ? `<span class="link-text">${
                        escapeHtml(feature.linkText)
                    }</span>`
                    : ""
            }</a>`
            : `<div class="VPFeature">${box}</div>`
    }</div>`;
}

function renderContentPage(content: string): string {
    return `<main class="VPPage"><div class="vp-doc container">${content}</div></main>`;
}

function renderDocsPage(
    page: Page,
    content: string,
    flatGuidePages: SidebarPage[],
    theme: ResolvedDocsThemeOptions,
    hasAside: boolean,
    chrome: PageChrome,
): string {
    const data = page.data as DocsData;
    const source = sourcePath(page).replace(/^\//, "");
    const activeIndex = flatGuidePages.findIndex((item) =>
        item.url === data.url
    );
    const previous = activeIndex > 0
        ? flatGuidePages[activeIndex - 1]
        : undefined;
    const next = activeIndex >= 0 && activeIndex < flatGuidePages.length - 1
        ? flatGuidePages[activeIndex + 1]
        : undefined;

    return `<div class="VPDoc has-sidebar${hasAside ? " has-aside" : ""}">
    <div class="container">
      ${
        hasAside
            ? `<aside class="aside"><div class="aside-curtain"></div><div class="aside-container"><div class="aside-content">${
                renderOutline(data, theme)
            }</div></div></aside>`
            : ""
    }
      <div class="content">
        <div class="content-container">
          <main class="main">
            <div class="vp-doc">
              ${
        theme.markdownMirrors
            ? `<div hidden aria-hidden="true">Are you an LLM? You can read better optimized documentation at ${
                markdownUrl(data.url)
            } for this page in Markdown format</div>`
            : ""
    }
              ${content}
            </div>
          </main>
          ${renderDocFooter(source, previous, next, data, theme, chrome)}
        </div>
      </div>
    </div>
  </div>`;
}

function renderSidebar(sidebar: SidebarSection[], currentUrl: string) {
    return `<aside class="VPSidebar"><div class="curtain"></div><nav class="nav" id="VPSidebarNav" aria-label="Sidebar Navigation">${
        sidebar.map((section) => renderSidebarSection(section, currentUrl))
            .join("")
    }</nav></aside><div class="VPSidebarBackdrop" aria-hidden="true"></div>`;
}

function renderSidebarSection(section: SidebarSection, currentUrl: string) {
    const hasActive = section.items.some((item) =>
        sidebarItemHasActive(item, currentUrl)
    );
    return `<div class="group"><section class="VPSidebarItem level-0${
        hasActive ? " has-active" : ""
    }"><div class="item"><div class="indicator"></div><h2 class="text">${
        escapeHtml(section.title)
    }</h2></div><div class="items">${
        section.items.map((item) => renderSidebarItem(item, currentUrl)).join(
            "",
        )
    }</div></section></div>`;
}

function renderSidebarItem(item: SidebarPage, currentUrl: string): string {
    const active = item.url === currentUrl;
    const hasActive = sidebarItemHasActive(item, currentUrl);
    const classes = [
        "VPSidebarItem",
        `level-${item.level}`,
        item.url ? "is-link" : "",
        active ? "is-active" : "",
        hasActive ? "has-active" : "",
        item.children.length ? "collapsible" : "",
    ].filter(Boolean).join(" ");

    const title = `<span class="label-text">${escapeHtml(item.title)}</span>${
        renderBadge(item.badge)
    }`;
    const label = item.url
        ? `<a class="link" href="${
            escapeAttribute(item.url)
        }"><p class="text">${title}</p></a>`
        : `<p class="text">${title}</p>`;

    if (!item.children.length) {
        return `<div class="${classes}"><div class="item"><div class="indicator"></div>${label}</div></div>`;
    }

    return `<details class="${classes}"${
        hasActive || !item.collapsed ? " open" : ""
    }><summary class="item"><div class="indicator"></div>${label}<span class="caret"></span></summary><div class="items">${
        item.children.map((child) => renderSidebarItem(child, currentUrl)).join(
            "",
        )
    }</div></details>`;
}

function sidebarItemHasActive(item: SidebarPage, currentUrl: string): boolean {
    return item.url === currentUrl ||
        item.children.some((child) => sidebarItemHasActive(child, currentUrl));
}

function renderOutline(data: DocsData, theme: ResolvedDocsThemeOptions) {
    const outline = resolveOutline(data, theme);
    if (!outline.enabled) {
        return "";
    }

    const headings = (data.headings ?? []).filter((heading) =>
        heading.level >= outline.min && heading.level <= outline.max
    );

    return `<div class="VPDocAside"><nav aria-labelledby="doc-outline-aria-label" class="VPDocAsideOutline"><div class="content"><div class="outline-marker"></div><div aria-level="2" class="outline-title" id="doc-outline-aria-label" role="heading">${
        escapeHtml(outline.label)
    }</div><ul class="VPDocOutlineItem root">${
        headings.map((heading) =>
            `<li class="outline-link level-${heading.level}"><a href="#${
                escapeAttribute(heading.slug)
            }">${escapeHtml(heading.text)}</a></li>`
        ).join("")
    }</ul></div></nav><div class="spacer"></div></div>`;
}

function renderDocFooter(
    sourcePath: string,
    previous: SidebarPage | undefined,
    next: SidebarPage | undefined,
    data: DocsData,
    theme: ResolvedDocsThemeOptions,
    chrome: PageChrome,
) {
    if (!chrome.docFooter) {
        return "";
    }

    const footerOptions = typeof data.docFooter === "object"
        ? data.docFooter
        : {};
    const previousPage = resolvePagerLink(
        data.prev !== undefined ? data.prev : footerOptions.prev,
        previous,
    );
    const nextPage = resolvePagerLink(
        data.next !== undefined ? data.next : footerOptions.next,
        next,
    );
    const editUrl = chrome.editLink && theme.editLink
        ? theme.editLink.pattern.replace(":path", sourcePath)
        : undefined;
    const lastUpdated = chrome.lastUpdated && data.lastUpdatedDate
        ? formatDate(data.lastUpdatedDate)
        : undefined;

    return `<footer class="VPDocFooter">
    ${
        editUrl || lastUpdated
            ? `<div class="edit-info">${
                editUrl
                    ? `<div class="edit-link"><a class="edit-link-button" href="${
                        escapeAttribute(editUrl)
                    }" target="_blank" rel="noreferrer"><span class="edit-link-icon"></span> ${
                        escapeHtml(
                            theme.editLink?.text ?? theme.labels.editLink,
                        )
                    }</a></div>`
                    : ""
            }${
                lastUpdated
                    ? `<div class="last-updated"><span>${
                        escapeHtml(
                            theme.lastUpdated
                                ? theme.lastUpdated.text
                                : theme.labels.lastUpdated,
                        )
                    }</span> <time datetime="${
                        data.lastUpdatedDate!.toISOString()
                    }">${escapeHtml(lastUpdated)}</time></div>`
                    : ""
            }</div>`
            : ""
    }
    <nav class="prev-next" aria-label="Pager">
      <div class="pager">${
        previousPage?.url
            ? `<a class="pager-link prev" href="${
                escapeAttribute(previousPage.url)
            }"><span class="desc">${
                escapeHtml(theme.labels.previousPage)
            }</span><span class="title">${
                escapeHtml(previousPage.title)
            }</span></a>`
            : ""
    }</div>
      <div class="pager">${
        nextPage?.url
            ? `<a class="pager-link next" href="${
                escapeAttribute(nextPage.url)
            }"><span class="desc">${
                escapeHtml(theme.labels.nextPage)
            }</span><span class="title">${
                escapeHtml(nextPage.title)
            }</span></a>`
            : ""
    }</div>
    </nav>
  </footer>`;
}

function resolvePagerLink(
    value: false | PageLink | undefined,
    fallback: SidebarPage | undefined,
): SidebarPage | undefined {
    if (value === false) {
        return undefined;
    }

    if (value === undefined) {
        return fallback;
    }

    if (typeof value === "string") {
        return {
            title: titleFromPath(value),
            url: normalizeHref(value),
            section: "",
            sectionOrder: 0,
            order: 0,
            level: 0,
            collapsed: false,
            children: [],
        };
    }

    const url = value.link ?? fallback?.url;
    if (!url) {
        return undefined;
    }

    return {
        title: value.text ?? fallback?.title ?? titleFromPath(url),
        url: normalizeHref(url),
        section: fallback?.section ?? "",
        sectionOrder: fallback?.sectionOrder ?? 0,
        order: fallback?.order ?? 0,
        level: fallback?.level ?? 0,
        collapsed: fallback?.collapsed ?? false,
        children: [],
    };
}
export function buildSearchIndex(
    entries: ContentEntry[],
    theme?: ResolvedDocsThemeOptions,
) {
    return entries
        .filter((entry) => entry.data.search !== false)
        .flatMap((entry) => searchSectionsForEntry(entry, theme));
}

const maxSearchTextLength = 5000;

function searchSectionsForEntry(
    entry: ContentEntry,
    theme?: ResolvedDocsThemeOptions,
) {
    const html = searchContentHtml(entry.html);
    const title = searchTitle(entry, theme);
    const url = entry.data.url;
    const headings = searchHeadingMatches(html, entry.data.headings ?? [])
        .filter((heading) => heading.level <= 3);
    const firstSectionHeading =
        headings.find((heading) => heading.level <= 2) ??
            headings[0];
    const homeText = homeSearchText(entry.data, theme);
    const rootText = boundSearchText(
        [
            title,
            homeText,
            htmlToText(
                html.slice(0, firstSectionHeading?.index ?? html.length),
            ),
        ]
            .filter(Boolean)
            .join(" "),
    );
    const sections = rootText
        ? [{
            id: url,
            url,
            anchor: "",
            title,
            titles: [title],
            text: rootText,
        }]
        : [];

    headings.forEach((heading, index) => {
        if (heading.level <= 1) {
            return;
        }

        const start = heading.index + heading.html.length;
        let end = html.length;
        for (const next of headings.slice(index + 1)) {
            if (next.level <= heading.level) {
                end = next.index;
                break;
            }
        }

        const text = boundSearchText(
            [heading.text, htmlToText(html.slice(start, end))]
                .filter(Boolean)
                .join(" "),
        );
        if (!text) {
            return;
        }

        sections.push({
            id: `${url}#${heading.slug}`,
            url,
            anchor: heading.slug,
            title: heading.text,
            titles: titlesForSearchHeading(
                title,
                heading,
                headings.slice(0, index),
            ),
            text,
        });
    });

    return sections;
}

function searchTitle(
    entry: ContentEntry,
    theme?: ResolvedDocsThemeOptions,
) {
    if (
        pageLayout(entry.data, entry.sourcePath, theme?.docsRoot ?? "guide") ===
            "home"
    ) {
        return entry.data.hero?.name ?? entry.data.title ?? theme?.title ??
            titleFromPath(entry.sourcePath);
    }

    return firstHeading(entry.data) ?? entry.data.title ??
        titleFromPath(entry.sourcePath);
}

function homeSearchText(
    data: DocsData,
    theme?: ResolvedDocsThemeOptions,
) {
    const parts: string[] = [];
    const hero = data.hero;
    if (hero) {
        parts.push(
            hero.name ?? theme?.title ?? "",
            hero.text ?? "",
            hero.tagline ?? "",
            hero.image?.alt ?? "",
            ...(hero.actions ?? []).map((action) => action.text),
        );
    }

    collectFeatureSearchText(parts, data.features ?? []);
    for (const section of data.homeSections ?? []) {
        parts.push(section.title ?? "", section.text ?? "");
        collectFeatureSearchText(parts, section.features ?? []);
    }

    return parts.filter(Boolean).join(" ");
}

function collectFeatureSearchText(parts: string[], features: Feature[]) {
    for (const feature of features) {
        parts.push(feature.title, feature.details, feature.linkText ?? "");
    }
}

function searchHeadingMatches(html: string, dataHeadings: Heading[]) {
    const headingsBySlug = new Map(
        dataHeadings.map((heading) => [heading.slug, heading]),
    );
    const pattern = /<h([1-6])\b[^>]*\bid="([^"]+)"[^>]*>([\s\S]*?)<\/h\1>/g;
    return [...html.matchAll(pattern)].map((match) => {
        const slug = decodeHtml(match[2]);
        const heading = headingsBySlug.get(slug);
        return {
            level: heading?.level ?? Number(match[1]),
            slug,
            text: heading?.text ?? htmlToText(match[3]),
            index: match.index ?? 0,
            html: match[0],
        };
    });
}

function titlesForSearchHeading(
    pageTitle: string,
    heading: { level: number; text: string },
    previousHeadings: Array<{ level: number; text: string }>,
) {
    const titles = [pageTitle].filter(Boolean);
    const parents: Array<{ level: number; text: string }> = [];

    for (const previous of previousHeadings) {
        while (
            parents.length &&
            parents[parents.length - 1].level >= previous.level
        ) {
            parents.pop();
        }

        if (previous.level > 1) {
            parents.push(previous);
        }
    }

    for (const parent of parents) {
        if (
            parent.level < heading.level &&
            parent.text &&
            parent.text !== titles[titles.length - 1]
        ) {
            titles.push(parent.text);
        }
    }

    if (heading.text && heading.text !== titles[titles.length - 1]) {
        titles.push(heading.text);
    }

    return titles;
}

function searchContentHtml(html: string) {
    return html
        .replace(/<script\b[\s\S]*?<\/script>/gi, " ")
        .replace(/<style\b[\s\S]*?<\/style>/gi, " ")
        .replace(/<template\b[\s\S]*?<\/template>/gi, " ")
        .replace(
            /<a\b[^>]*class="[^"]*\bheader-anchor\b[^"]*"[\s\S]*?<\/a>/gi,
            " ",
        )
        .replace(
            /<button\b[^>]*class="[^"]*\bcopy\b[^"]*"[\s\S]*?<\/button>/gi,
            " ",
        )
        .replace(
            /<div\b[^>]*class="[^"]*\bline-numbers-wrapper\b[^"]*"[\s\S]*?<\/div>/gi,
            " ",
        )
        .replace(
            /<([a-z][\w:-]*)\b(?=[^>]*(?:\shidden(?:[\s=>]|$)|\saria-hidden=["']true["']))[^>]*>[\s\S]*?<\/\1>/gi,
            " ",
        )
        .replace(/<[^>]+\b(?:hidden|aria-hidden=["']true["'])[^>]*\/?>/gi, " ");
}

function boundSearchText(value: string) {
    const text = value.replace(/\s+/g, " ").trim();
    if (text.length <= maxSearchTextLength) {
        return text;
    }

    return `${text.slice(0, maxSearchTextLength).trim()}...`;
}
export function renderLlmsIndex(
    pages: SidebarPage[],
    theme: ResolvedDocsThemeOptions,
    home?: DocsData,
) {
    const sections = new Map<string, SidebarPage[]>();

    for (const page of pages.filter((page) => page.url)) {
        const section = page.section || "Other";
        const entries = sections.get(section) ?? [];
        entries.push(page);
        sections.set(section, entries);
    }

    const tableOfContents = [...sections.entries()].map((
        [section, sectionPages],
    ) => `### ${section}\n\n${
        sectionPages.map((page) =>
            `- [${page.title}](${markdownUrl(page.url!)})`
        ).join("\n")
    }`).join("\n\n");

    return `# ${theme.title} Documentation\n\n${
        llmsIntro(theme, home)
    }\n\n## Table of Contents\n\n${tableOfContents}\n`;
}

function llmsIntro(theme: ResolvedDocsThemeOptions, home?: DocsData) {
    const quote = home?.hero?.text ?? theme.description;
    const tagline = home?.hero?.tagline;

    return [`> ${quote}`, tagline].filter(Boolean).join("\n\n");
}

export function renderLlmsFull(
    entries: ContentEntry[],
    pages: SidebarPage[],
    _theme: ResolvedDocsThemeOptions,
) {
    const byUrl = new Map(entries.map((entry) => [entry.data.url, entry]));
    const sections = pages.filter((page) => page.url).map((page) => {
        const entry = byUrl.get(page.url!);
        if (!entry) {
            return "";
        }

        return renderMarkdownMirror(entry);
    }).filter(Boolean);

    return sections.join("\n---\n\n");
}

export function renderMarkdownMirror(entry: ContentEntry) {
    return `---\nurl: ${markdownUrl(entry.data.url)}\n---\n${
        llmsMarkdown(entry.markdown)
    }\n`;
}

function llmsMarkdown(markdown: string) {
    return stripFrontMatter(markdown).trim();
}

function stripFrontMatter(markdown: string) {
    const normalized = markdown.replace(/\r\n/g, "\n");
    if (!normalized.startsWith("---\n")) {
        return normalized;
    }

    const end = normalized.indexOf("\n---\n", 4);
    if (end === -1) {
        return normalized;
    }

    return normalized.slice(end + 5);
}

function markdownUrl(url: string) {
    return markdownMirrorUrl(url);
}

function prepareMetas(
    data: DocsData,
    title: string,
    description: string,
    theme: ResolvedDocsThemeOptions,
) {
    data.metas = {
        site: theme.title,
        title,
        description,
        icon: theme.brand.icon,
        image: data.image,
        color: theme.brand.themeColor,
        lang: data.lang ?? "en-US",
        generator: "Lume docs theme",
        ...theme.metas,
        ...(data.metas ?? {}),
    };
}

function coreMetaHead(options: {
    title: string;
    description: string;
    data: DocsData;
    theme: ResolvedDocsThemeOptions;
}): HeadEntry[] {
    const metas = options.data.metas ?? {};
    const themeColor = typeof metas.color === "string"
        ? metas.color
        : options.theme.brand.themeColor;

    return [
        ["meta", { name: "description", content: options.description }],
        ["meta", { name: "theme-color", content: themeColor }],
    ];
}

function canonicalHead(
    data: DocsData,
    theme: ResolvedDocsThemeOptions,
): HeadEntry[] {
    if (data.canonical === false || !theme.location) {
        return [];
    }

    return [["link", {
        rel: "canonical",
        href: data.canonical || absoluteUrl(data.url, theme),
    }]];
}

type PageChrome = {
    sidebar: boolean;
    aside: boolean;
    footer: boolean;
    docFooter: boolean;
    editLink: boolean;
    lastUpdated: boolean;
};

function pageChrome(data: DocsData, layout: string | false): PageChrome {
    const docs = layout === "docs";
    const docFooterOptions = typeof data.docFooter === "object"
        ? data.docFooter
        : {};
    const docFooter = data.docFooter !== false;
    return {
        sidebar: docs && data.sidebar !== false,
        aside: docs && data.aside !== false,
        footer: data.footer !== false,
        docFooter,
        editLink: docFooter && data.editLink !== false &&
            docFooterOptions.editLink !== false,
        lastUpdated: docFooter && data.lastUpdated !== false &&
            docFooterOptions.lastUpdated !== false,
    };
}

function resolveOutline(data: DocsData, theme: ResolvedDocsThemeOptions) {
    if (data.outline === false) {
        return { enabled: false, min: 2, max: 3, label: theme.labels.outline };
    }

    const configured =
        typeof data.outline === "object" && !Array.isArray(data.outline)
            ? data.outline
            : typeof data.outline === "number" || Array.isArray(data.outline)
            ? { level: data.outline }
            : theme.outline;
    const [min, max] = outlineRange(
        configured.level ?? theme.outline.level ?? 2,
    );
    return {
        enabled: true,
        min,
        max,
        label: configured.label ?? theme.outline.label,
    };
}

function outlineRange(level: OutlineLevel): [number, number] {
    return Array.isArray(level) ? level : [level, level];
}

function replaceToc(
    html: string,
    data: DocsData,
    theme: ResolvedDocsThemeOptions,
) {
    if (!html.includes("data-toc-placeholder")) {
        return html;
    }

    const outline = resolveOutline(data, theme);
    const headings = (data.headings ?? []).filter((heading) =>
        heading.level >= outline.min && heading.level <= outline.max
    );
    const toc = headings.length
        ? `<nav class="table-of-contents"><ul>${
            headings.map((heading) =>
                `<li class="level-${heading.level}"><a href="#${
                    escapeAttribute(heading.slug)
                }">${escapeHtml(heading.text)}</a></li>`
            ).join("")
        }</ul></nav>`
        : "";

    return html.replace(
        /<nav class="table-of-contents" data-toc-placeholder><\/nav>/g,
        toc,
    );
}

function renderHeadEntries(entries: HeadEntry[]) {
    return entries.map((entry) => {
        if (typeof entry === "string") {
            return entry;
        }

        const [tag, attrs = {}, content] = entry;
        const attributes = renderAttrs(attrs);
        if (voidHeadTags.has(tag)) {
            return `<${tag}${attributes}>`;
        }

        return `<${tag}${attributes}>${content ?? ""}</${tag}>`;
    }).join("\n  ");
}

function renderAttrs(attrs: HeadAttrs) {
    const rendered = Object.entries(attrs)
        .filter(([, value]) => value !== undefined && value !== false)
        .map(([name, value]) =>
            value === true
                ? name
                : `${name}="${escapeAttribute(String(value))}"`
        );

    return rendered.length ? ` ${rendered.join(" ")}` : "";
}

function formatDate(date: Date) {
    return new Intl.DateTimeFormat("en-US", {
        year: "numeric",
        month: "short",
        day: "numeric",
    }).format(date);
}

function absoluteUrl(path: string, theme: ResolvedDocsThemeOptions) {
    if (!theme.location) {
        return path;
    }

    return new URL(path, theme.location).href;
}

const voidHeadTags = new Set(["base", "link", "meta"]);
