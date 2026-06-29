const root = document.documentElement;
const navBar = document.querySelector(".VPNavBar");
const darkToggles = document.querySelectorAll(".VPSwitchAppearance");
const searchButton = document.querySelector(".VPNavBarSearchButton");
const hamburger = document.querySelector(".VPNavBarHamburger");
const navScreen = document.querySelector(".VPNavScreen");
const localMenu = document.querySelector(".VPLocalNav .menu");
const sidebar = document.querySelector(".VPSidebar");
const sidebarBackdrop = document.querySelector(".VPSidebarBackdrop");
const basePath = normalizeBasePath(window.__LUME_DOCS_BASE_PATH__ || "/");
const labels = window.__LUME_DOCS_LABELS__ || {};
const searchStateKey = "lume-docs-search-state";
let activeSearchButton = null;

function syncNavTop() {
    if (!navBar?.classList.contains("home")) {
        return;
    }

    navBar.classList.toggle("top", window.scrollY < 1);
}

syncNavTop();
window.addEventListener("scroll", syncNavTop, { passive: true });

function syncThemeControls() {
    const checked = root.classList.contains("dark") ? "true" : "false";
    darkToggles.forEach((toggle) => {
        toggle.setAttribute("aria-checked", checked);
    });
}

function setTheme(theme) {
    localStorage.setItem("lume-docs-theme", theme);
    root.classList.toggle("dark", theme === "dark");
    syncThemeControls();
}

syncThemeControls();
darkToggles.forEach((toggle) => {
    toggle.addEventListener("click", () => {
        setTheme(root.classList.contains("dark") ? "light" : "dark");
    });
});

function setSidebarOpen(open) {
    sidebar?.classList.toggle("open", open);
    localMenu?.setAttribute("aria-expanded", open ? "true" : "false");
    root.classList.toggle("sidebar-open", open);
}

function setMobileNavOpen(open) {
    if (!navScreen) {
        return;
    }

    if (open) {
        setSidebarOpen(false);
    }

    navScreen.classList.toggle("open", open);
    hamburger?.classList.toggle("active", open);
    navBar?.classList.toggle("screen-open", open);
    hamburger?.setAttribute("aria-expanded", open ? "true" : "false");
    root.classList.toggle("nav-screen-open", open);
}

function toggleMobileNav() {
    setMobileNavOpen(!navScreen?.classList.contains("open"));
}

function toggleSidebar() {
    const open = !sidebar?.classList.contains("open");
    if (open) {
        setMobileNavOpen(false);
    }
    setSidebarOpen(open);
}

hamburger?.addEventListener("click", toggleMobileNav);
navScreen?.addEventListener("click", (event) => {
    if (event.target?.closest?.("a")) {
        setMobileNavOpen(false);
    }
});
localMenu?.addEventListener("click", toggleSidebar);
sidebarBackdrop?.addEventListener("click", () => setSidebarOpen(false));
sidebar?.addEventListener("click", (event) => {
    if (event.target?.closest?.("a")) {
        setSidebarOpen(false);
    }
});
document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") {
        return;
    }
    if (sidebar?.classList.contains("open")) {
        setSidebarOpen(false);
    }
    if (navScreen?.classList.contains("open")) {
        setMobileNavOpen(false);
    }
});
window.addEventListener("resize", () => {
    if (window.innerWidth >= 768) {
        setMobileNavOpen(false);
    }
    if (window.innerWidth >= 960) {
        setSidebarOpen(false);
    }
}, { passive: true });
document.querySelector(".return-top")?.addEventListener("click", () => {
    window.scrollTo({ top: 0, behavior: "smooth" });
});

const copyTimeouts = new WeakMap();

document.querySelectorAll(".copy").forEach((button) => {
    button.addEventListener("click", async () => {
        const wrapper = button.parentElement;
        const pre = wrapper?.querySelector("pre");
        if (!wrapper || !pre) {
            return;
        }

        const clone = pre.cloneNode(true);
        clone.querySelectorAll(".vp-copy-ignore, .diff.remove").forEach((
            node,
        ) => node.remove());
        let code = codeTextFromPre(clone);
        if (isShellLanguage(wrapper.className)) {
            code = code.replace(/^\s*(\$|>)\s/gm, "").trim();
        }

        await copyToClipboard(code);
        button.classList.add("copied");
        clearTimeout(copyTimeouts.get(button));
        copyTimeouts.set(
            button,
            setTimeout(() => {
                button.classList.remove("copied");
                button.blur();
                copyTimeouts.delete(button);
            }, 2000),
        );
    });
});

function codeTextFromPre(pre) {
    const lines = [...pre.querySelectorAll(".line")];
    if (lines.length > 0) {
        return lines.map((line) =>
            line.textContent?.replace(/\u200b/g, "") ?? ""
        ).join("\n");
    }

    pre.innerHTML = pre.innerHTML.replace(/\n+/g, "\n");
    return pre.textContent || "";
}

document.querySelectorAll(".vp-code-group").forEach((group) => {
    const blocks = [...group.children].filter((child) =>
        child.matches("div[class^='language-'], div[class*=' language-']")
    );

    if (blocks.length < 2) {
        return;
    }

    const tabs = document.createElement("div");
    tabs.className = "tabs";

    blocks.forEach((block, index) => {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = block.dataset.title ||
            block.querySelector(".lang")?.textContent || `Example ${index + 1}`;
        button.className = index === 0 ? "active" : "";
        block.hidden = index !== 0;
        block.classList.toggle("active", index === 0);

        button.addEventListener("click", () => {
            tabs.querySelectorAll("button").forEach((tab) =>
                tab.classList.remove("active")
            );
            button.classList.add("active");
            blocks.forEach((candidate) => {
                const active = candidate === block;
                candidate.hidden = !active;
                candidate.classList.toggle("active", active);
            });
        });

        tabs.append(button);
    });

    group.prepend(tabs);
    group.classList.add("enhanced");
});

let searchIndexPromise;
const searchPageDocumentPromises = new Map();

function getSearchIndex() {
    searchIndexPromise ??= fetch(withBasePath("/search-index.json")).then(
        (response) => response.json(),
    );
    return searchIndexPromise;
}

function closeSearch() {
    document.querySelector(".VPLocalSearchBox")?.remove();
    root.classList.remove("search-open");
    document.body.classList.remove("search-open");
    activeSearchButton?.focus();
    activeSearchButton = null;
}

function openSearch() {
    setMobileNavOpen(false);
    const existing = document.querySelector(".VPLocalSearchBox");
    if (existing) {
        focusSearchInput(existing.querySelector(".search-input"));
        return;
    }

    activeSearchButton = document.activeElement;
    root.classList.add("search-open");
    document.body.classList.add("search-open");
    const searchState = getSearchState();
    const shell = document.createElement("div");
    shell.className = "VPLocalSearchBox";
    shell.innerHTML = `
    <div class="backdrop"></div>
    <div class="shell" role="dialog" aria-modal="true" aria-label="${
        escapeHtml(labels.search || "Search")
    }">
      <form class="search-bar" role="search">
        <button class="search-back" type="button" aria-label="Close search" title="Close search"><span class="vpi-arrow-left local-search-icon" aria-hidden="true"></span></button>
        <label class="search-label" aria-label="${
        escapeHtml(labels.search || "Search")
    }"><span class="vpi-search search-icon local-search-icon" aria-hidden="true"></span></label>

        <input class="search-input" type="text" role="searchbox" inputmode="search" spellcheck="false" placeholder="${
        escapeHtml(labels.searchPlaceholder || labels.search || "Search")
    }" autocomplete="off">
        <div class="search-actions"><button class="toggle-layout-button" type="button" aria-label="Display detailed list" title="Display detailed list" aria-pressed="false"><span class="vpi-layout-list local-search-icon" aria-hidden="true"></span></button><button class="clear-button" type="button" aria-label="Clear search" title="Clear search"><span class="vpi-delete local-search-icon" aria-hidden="true"></span></button></div>
      </form>
      <ul class="results" role="listbox"></ul>
      <div class="search-keyboard-shortcuts" aria-hidden="true"><span><kbd><span class="vpi-arrow-up navigate-icon"></span></kbd><kbd><span class="vpi-arrow-down navigate-icon"></span></kbd> to navigate</span><span><kbd><span class="vpi-corner-down-left navigate-icon"></span></kbd> to select</span><span><kbd>esc</kbd> to close</span></div>
    </div>`;
    document.body.append(shell);

    const input = shell.querySelector(".search-input");
    const results = shell.querySelector(".results");
    const toggleLayoutButton = shell.querySelector(".toggle-layout-button");
    let selectedIndex = 0;
    let detailedList = searchState.detailedList;
    let searchRenderId = 0;
    input.value = searchState.query;

    const render = async () => {
        const query = input.value.trim();
        const tokens = tokenize(query);
        const currentRenderId = ++searchRenderId;
        selectedIndex = 0;

        if (!tokens.length) {
            results.innerHTML = "";
            updateSelectedResult(results, selectedIndex);
            return;
        }

        const index = await getSearchIndex();
        if (currentRenderId !== searchRenderId) {
            return;
        }

        const matches = buildSearchCandidates(index, tokens)
            .filter((item) => item.score > 0)
            .sort((left, right) => right.score - left.score)
            .slice(0, 16);

        results.innerHTML = matches.length
            ? matches.map((result, index) =>
                renderSearchResult(result, tokens, index, detailedList)
            )
                .join("")
            : `<li class="no-results">${
                escapeHtml(labels.searchNoResults || "No results found.")
            }</li>`;
        updateSelectedResult(results, selectedIndex);

        if (matches.length && detailedList) {
            hydrateDetailedExcerpts(
                matches,
                tokens,
                results,
                () => currentRenderId === searchRenderId && detailedList,
            );
        }
    };
    const syncDetailedList = () => {
        toggleLayoutButton?.classList.toggle("detailed-list", detailedList);
        toggleLayoutButton?.setAttribute(
            "aria-pressed",
            detailedList ? "true" : "false",
        );
        toggleLayoutButton?.setAttribute(
            "aria-label",
            detailedList ? "Display simple list" : "Display detailed list",
        );
        toggleLayoutButton?.setAttribute(
            "title",
            detailedList ? "Display simple list" : "Display detailed list",
        );
        results.classList.toggle("detailed", detailedList);
    };

    shell.querySelector(".backdrop").addEventListener("click", closeSearch);
    shell.querySelector(".search-back").addEventListener("click", closeSearch);
    shell.querySelector(".clear-button").addEventListener("click", () => {
        input.value = "";
        saveSearchState({ query: "" });
        render();
        input.focus();
    });
    toggleLayoutButton?.addEventListener("click", () => {
        detailedList = !detailedList;
        saveSearchState({ detailedList });
        syncDetailedList();
        render();
        input.focus();
    });
    input.addEventListener("input", () => {
        saveSearchState({ query: input.value });
        render();
    });
    results.addEventListener("click", (event) => {
        const target = event.target;
        const recentButton = target?.closest?.("[data-recent-search]");
        if (recentButton) {
            input.value = recentButton.dataset.recentSearch || "";
            saveSearchState({ query: input.value });
            input.focus();
            render();
            return;
        }

        if (target?.closest?.("[data-search-result]") && input.value.trim()) {
            saveRecentSearch(input.value.trim());
            saveSearchState({ query: input.value });
        }
    });
    results.addEventListener("mouseover", (event) => {
        const target = event.target?.closest?.("[data-search-result]");
        if (!target || !results.contains(target)) {
            return;
        }

        const links = [...results.querySelectorAll("[data-search-result]")];
        const index = links.indexOf(target);
        if (index >= 0 && index !== selectedIndex) {
            selectedIndex = index;
            updateSelectedResult(results, selectedIndex, { scroll: false });
        }
    });
    shell.addEventListener("keydown", (event) => {
        const links = [...results.querySelectorAll("[data-search-result]")];
        if (event.key === "ArrowDown") {
            event.preventDefault();
            selectedIndex = Math.min(selectedIndex + 1, links.length - 1);
            updateSelectedResult(results, selectedIndex);
        } else if (event.key === "ArrowUp") {
            event.preventDefault();
            selectedIndex = Math.max(selectedIndex - 1, 0);
            updateSelectedResult(results, selectedIndex);
        } else if (event.key === "Enter" && links[selectedIndex]) {
            event.preventDefault();
            if (input.value.trim()) {
                saveRecentSearch(input.value.trim());
                saveSearchState({ query: input.value });
            }
            links[selectedIndex].click();
        }
    });
    syncDetailedList();
    focusSearchInput(input);
    render();
}

searchButton?.addEventListener("click", openSearch);
window.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
        closeSearch();
        setMobileNavOpen(false);
    }

    if (
        (event.key === "/" ||
            event.key.toLowerCase() === "k" &&
                (event.ctrlKey || event.metaKey)) &&
        !/input|textarea/i.test(document.activeElement?.tagName ?? "")
    ) {
        event.preventDefault();
        openSearch();
    }
});

if (document.querySelector(".mermaid")) {
    import("https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs")
        .then(({ default: mermaid }) => {
            mermaid.initialize({
                startOnLoad: true,
                theme: root.classList.contains("dark") ? "dark" : "default",
            });
        });
}

async function copyToClipboard(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        const element = document.createElement("textarea");
        const previouslyFocusedElement = document.activeElement;
        element.value = text;
        element.setAttribute("readonly", "");
        element.style.contain = "strict";
        element.style.position = "absolute";
        element.style.left = "-9999px";
        element.style.fontSize = "12pt";
        const selection = document.getSelection();
        const originalRange = selection && selection.rangeCount > 0
            ? selection.getRangeAt(0)
            : null;
        document.body.appendChild(element);
        element.select();
        element.selectionStart = 0;
        element.selectionEnd = text.length;
        document.execCommand("copy");
        document.body.removeChild(element);
        if (selection && originalRange) {
            selection.removeAllRanges();
            selection.addRange(originalRange);
        }
        previouslyFocusedElement?.focus?.();
    }
}

function isShellLanguage(className) {
    const match = /language-([\w-]+)/.exec(className);
    return /^(bash|sh|shell|zsh|console|powershell|ps1|cmd|bat)$/i.test(
        match?.[1] || "",
    );
}
function renderSearchResult(result, tokens, index, detailedList = false) {
    const item = result.item;
    const url = result.url || item.url;
    const selected = index === 0 ? " selected" : "";
    const crumbs = result.crumbs?.length
        ? result.crumbs
        : Array.isArray(item.titles) && item.titles.length
        ? item.titles
        : [item.title].filter(Boolean);

    const titles = crumbs.map((value, crumbIndex) => {
        const isLast = crumbIndex === crumbs.length - 1;
        const icon = crumbIndex === 0
            ? '<span class="title-icon">#</span>'
            : '<span class="vpi-chevron-right local-search-icon" aria-hidden="true"></span>';
        return `${icon}<span class="title${
            isLast ? " main" : ""
        }"><span class="text">${highlightText(value, tokens)}</span></span>`;
    }).join("");
    const excerpt = detailedList
        ? renderDetailedExcerpt(result, tokens, index)
        : "";

    return `<li><a class="result${selected}" data-search-result role="option" aria-selected="${
        index === 0 ? "true" : "false"
    }" href="${
        escapeHtml(withBasePath(url))
    }"><div><div class="titles">${titles}</div>${excerpt}</div></a></li>`;
}

function renderDetailedExcerpt(result, tokens, index) {
    return `<div class="excerpt-wrapper" data-search-excerpt="${index}"><div class="excerpt loading"><div class="vp-doc"><p>${
        highlightText(
            excerptFor(result.segment || result.item.text || "", tokens),
            tokens,
        )
    }</p></div></div></div>`;
}

async function hydrateDetailedExcerpts(matches, tokens, results, isCurrent) {
    await Promise.all(matches.map(async (result, index) => {
        const target = results.querySelector(
            `[data-search-excerpt="${index}"]`,
        );
        if (!target) {
            return;
        }

        try {
            const html = await richExcerptForResult(result, tokens);
            if (!isCurrent() || !target.isConnected) {
                return;
            }

            target.innerHTML = html
                ? richDetailedExcerpt(html)
                : plainDetailedExcerpt(result, tokens);
        } catch {
            if (isCurrent() && target.isConnected) {
                target.innerHTML = plainDetailedExcerpt(result, tokens);
            }
        }
    }));
}

async function richExcerptForResult(result, tokens) {
    const doc = await searchPageDocument(result.item.url);
    const content = doc.querySelector(".vp-doc");
    if (!content) {
        return "";
    }

    const nodes = searchSectionNodes(content, result.item.anchor);
    if (!nodes.length) {
        return "";
    }

    const fragment = excerptFragment(nodes, tokens);
    sanitizeSearchExcerpt(fragment);
    highlightFragmentText(fragment, tokens);
    return fragment.innerHTML.trim();
}

function plainDetailedExcerpt(result, tokens) {
    const excerpt = excerptFor(
        result.segment || result.item.text || "",
        tokens,
    );
    if (!excerpt) {
        return "";
    }

    return `<div class="excerpt"><div class="vp-doc"><p>${
        highlightText(excerpt, tokens)
    }</p></div></div>`;
}

function richDetailedExcerpt(html) {
    return `<div class="excerpt" inert><div class="vp-doc">${html}</div></div><div class="excerpt-gradient-bottom"></div><div class="excerpt-gradient-top"></div>`;
}

function searchPageDocument(url) {
    const pageUrl = withBasePath(String(url || "/").split("#")[0] || "/");
    if (!searchPageDocumentPromises.has(pageUrl)) {
        searchPageDocumentPromises.set(
            pageUrl,
            fetch(pageUrl)
                .then((response) => {
                    if (!response.ok) {
                        throw new Error(`Unable to load ${pageUrl}`);
                    }
                    return response.text();
                })
                .then((html) =>
                    new DOMParser().parseFromString(html, "text/html")
                ),
        );
    }

    return searchPageDocumentPromises.get(pageUrl);
}

function searchSectionNodes(content, anchor) {
    const heading = anchor ? findHeadingById(content, anchor) : null;
    const nodes = [];
    let current = heading
        ? heading.nextElementSibling
        : content.firstElementChild;
    const level = heading ? headingLevel(heading) : 1;

    while (current) {
        const currentLevel = headingLevel(current);
        if (currentLevel && currentLevel <= level) {
            break;
        }

        if (!current.matches?.("[hidden], .visually-hidden")) {
            nodes.push(current);
        }
        current = current.nextElementSibling;
    }

    return nodes;
}

function findHeadingById(content, id) {
    return [
        ...content.querySelectorAll(
            "h1[id], h2[id], h3[id], h4[id], h5[id], h6[id]",
        ),
    ]
        .find((heading) => heading.id === id) ?? null;
}

function headingLevel(element) {
    const match = /^H([1-6])$/.exec(element?.tagName || "");
    return match ? Number(match[1]) : 0;
}

function excerptFragment(nodes, tokens) {
    const fragment = document.createElement("div");
    const targetIndex = Math.max(
        0,
        nodes.findIndex((node) =>
            tokens.some((token) =>
                node.textContent.toLowerCase().includes(token)
            )
        ),
    );
    const start = Math.max(0, targetIndex - 1);
    let textLength = 0;

    for (const node of nodes.slice(start)) {
        const clone = node.cloneNode(true);
        fragment.append(clone);
        textLength += clone.textContent.trim().length;

        if (
            textLength >= 900 ||
            fragment.querySelector("div[class*='language-']") &&
                textLength >= 180
        ) {
            break;
        }
    }

    return fragment;
}

function sanitizeSearchExcerpt(fragment) {
    fragment.querySelectorAll(
        "script, style, iframe, object, embed, .copy, [hidden], .visually-hidden",
    )
        .forEach((element) => element.remove());
}

function highlightFragmentText(fragment, tokens) {
    if (!tokens.length) {
        return;
    }

    const matcher = new RegExp(`(${tokens.map(escapeRegExp).join("|")})`, "ig");
    const walker = document.createTreeWalker(fragment, NodeFilter.SHOW_TEXT, {
        acceptNode(node) {
            const text = node.nodeValue || "";
            const parent = node.parentElement;
            if (
                !text.trim() ||
                parent?.closest("mark, script, style") ||
                !tokens.some((token) => text.toLowerCase().includes(token))
            ) {
                return NodeFilter.FILTER_REJECT;
            }

            return NodeFilter.FILTER_ACCEPT;
        },
    });
    const matches = [];
    while (walker.nextNode()) {
        matches.push(walker.currentNode);
    }

    matches.forEach((node) => {
        const parts = (node.nodeValue || "").split(matcher);
        const replacement = document.createDocumentFragment();
        parts.forEach((part) => {
            if (!part) {
                return;
            }
            if (tokens.some((token) => part.toLowerCase() === token)) {
                const mark = document.createElement("mark");
                mark.textContent = part;
                replacement.append(mark);
            } else {
                replacement.append(document.createTextNode(part));
            }
        });
        node.parentNode?.replaceChild(replacement, node);
    });
}
function getRecentSearches() {
    try {
        return JSON.parse(
            localStorage.getItem("lume-docs-recent-searches") || "[]",
        )
            .filter((value) => typeof value === "string")
            .slice(0, 5);
    } catch {
        return [];
    }
}

function saveRecentSearch(query) {
    const normalized = query.trim();
    if (!normalized) {
        return;
    }

    const next = [
        normalized,
        ...getRecentSearches().filter((value) => value !== normalized),
    ].slice(0, 5);
    localStorage.setItem("lume-docs-recent-searches", JSON.stringify(next));
}

function getSearchState() {
    try {
        const state = JSON.parse(localStorage.getItem(searchStateKey) || "{}");
        return {
            query: typeof state.query === "string" ? state.query : "",
            detailedList: Boolean(state.detailedList),
        };
    } catch {
        return { query: "", detailedList: false };
    }
}

function saveSearchState(nextState) {
    try {
        localStorage.setItem(
            searchStateKey,
            JSON.stringify({ ...getSearchState(), ...nextState }),
        );
    } catch {
        // Ignore storage failures; search still works without persistence.
    }
}

function focusSearchInput(input) {
    input?.focus();
    if (input?.value) {
        input.select();
    }
}

function renderRecentSearches(recent) {
    return `<li class="recent-searches"><div class="recent-title">${
        escapeHtml(labels.searchRecent || "Recent searches")
    }</div><div class="recent-items">${
        recent.map((query) =>
            `<button type="button" data-recent-search="${escapeHtml(query)}">${
                escapeHtml(query)
            }</button>`
        ).join("")
    }</div></li>`;
}
function buildSearchCandidates(index, tokens) {
    return index.map((item) => scoreSearchResult(item, tokens));
}

function scoreSearchResult(item, tokens) {
    const title = String(item.title || "").toLowerCase();
    const titles = Array.isArray(item.titles) && item.titles.length
        ? item.titles
        : [item.title].filter(Boolean);
    const breadcrumbText = titles.join(" ").toLowerCase();
    const text = String(item.text || "").toLowerCase();
    let score = 0;

    for (const token of tokens) {
        const present = title.includes(token) ||
            breadcrumbText.includes(token) || text.includes(token);
        if (!present) {
            return { item, crumbs: titles, score: 0 };
        }

        if (title === token) score += 170;
        if (title.startsWith(token)) score += 120;
        if (title.includes(token)) score += 90;
        if (breadcrumbText.includes(token)) score += 45;
        if (text.includes(token)) score += 10;
    }

    return {
        item,
        crumbs: titles,
        score,
        segment: item.text || "",
        url: item.anchor ? `${item.url}#${item.anchor}` : item.url,
    };
}
function updateSelectedResult(results, index, options = {}) {
    const { scroll = true } = options;
    const links = [...results.querySelectorAll("[data-search-result]")];
    links.forEach((link, current) => {
        link.classList.toggle("selected", current === index);
        link.setAttribute(
            "aria-selected",
            current === index ? "true" : "false",
        );
    });
    if (scroll) {
        links[index]?.scrollIntoView({ block: "nearest" });
    }
}

function tokenize(value) {
    return value.toLowerCase().split(/\s+/).filter(Boolean);
}

function excerptFor(text, tokens) {
    if (!tokens.length) {
        return text.slice(0, 180);
    }

    const lower = text.toLowerCase();
    const position = Math.max(
        0,
        Math.min(
            ...tokens.map((token) => {
                const index = lower.indexOf(token);
                return index === -1 ? Number.POSITIVE_INFINITY : index;
            }).filter(Number.isFinite),
        ) - 64,
    );
    const excerpt = text.slice(position, position + 220).trim();
    return `${position > 0 ? "..." : ""}${excerpt}${
        position + 220 < text.length ? "..." : ""
    }`;
}

function highlightText(value, tokens) {
    let html = escapeHtml(value);
    for (const token of tokens) {
        html = html.replace(
            new RegExp(`(${escapeRegExp(escapeHtml(token))})`, "ig"),
            "<mark>$1</mark>",
        );
    }
    return html;
}

function withBasePath(value) {
    if (
        !value ||
        value.startsWith("#") ||
        /^[a-z][a-z0-9+.-]*:/i.test(value) ||
        value.startsWith("//") ||
        basePath === "/"
    ) {
        return value;
    }

    if (value.startsWith(basePath)) {
        return value;
    }

    return value.startsWith("/")
        ? `${basePath.slice(0, -1)}${value}`
        : `${basePath}${value}`;
}

function normalizeBasePath(value) {
    const normalized = `/${String(value).replace(/^\/+|\/+$/g, "")}/`.replace(
        /\/+/g,
        "/",
    );
    return normalized === "//" ? "/" : normalized;
}

function escapeRegExp(value) {
    return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function escapeHtml(value) {
    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}
