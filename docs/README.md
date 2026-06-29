# Foundatio Mediator Documentation

This folder contains the Lume/Deno documentation site for Foundatio Mediator.
The reusable theme in `_theme/` is VitePress-inspired, but the implementation is
Lume-native: pages are normal Lume pages, navigation is generated from the file
system and front matter, and projects can add non-doc pages without fighting a
docs-only framework.

## Development

```bash
cd docs
deno task dev
```

The generated site is served at `http://localhost:5173/`.

## Building

```bash
deno task build
```

The production site is written to `docs/_site`.

## Site Config

Project-specific settings live in `_config.ts`:

```ts
import lume from "lume/mod.ts";
import docsTheme from "./_theme/mod.ts";

const location = new URL("https://mediator.foundatio.dev");
const site = lume({ location });

site.use(docsTheme({
    title: "Foundatio Mediator",
    description: "Blazingly fast C# mediator powered by source generators",
    location,
    docsRoot: "guide",
    lastUpdated: { git: true },
}));

site.copy("public", ".");

export default site;
```

The theme owns the reusable shell, sidebar generation, markdown rendering,
search index, `llms.txt`, markdown mirrors, SEO metadata, sitemap generation,
URL checking, code highlighting, and static assets.

## Adding Docs

Add markdown files under `guide/`. The sidebar and previous/next pager are
generated from file paths and front matter:

```yaml
---
title: "Getting Started"
nav:
  section: "Introduction"
  sectionOrder: 10
  order: 20
  collapsed: false
---
```

Use `nav.section` to group pages, `nav.sectionOrder` to sort groups, and
`nav.order` to sort pages inside a group. Nested folders become nested sidebar
items automatically. Set `nav.hidden: true` to keep a page out of the sidebar
while still rendering it.

If front matter is missing, the theme falls back to the first heading or the
file name.

## Page Chrome

Markdown pages can control the theme shell with front matter:

```yaml
---
layout: docs
sidebar: true
aside: true
footer: true
editLink: true
lastUpdated: true
outline:
  level: 2
---
```

Supported `layout` values are:

- `home` for the front page with hero/actions/features.
- `docs` for guide pages with sidebar and page outline.
- `page` for standalone markdown pages wrapped in the site chrome.
- `false` to opt out of theme rendering.

Set `navbar`, `sidebar`, `aside`, `footer`, `editLink`, or `lastUpdated` to
`false` on any page to hide that part of the chrome.

## Custom HTML Pages

Put fully custom HTML pages in `pages/`. The theme copies that folder to the
site root without wrapping it in the docs shell.

Use this for launch pages, benchmarks, campaign pages, or any page that should
have its own structure and styling. Keep files in this folder intentional:
anything placed here becomes public site output.

Raw pages are intentionally outside the theme pipeline. They do not receive the
shared SEO metadata, base-path rewriting, search indexing, nav/footer, or theme
assets unless the page includes those pieces manually.

Set `rawPagesDir: false` in `docsTheme()` to disable this convention, or set it
to another folder name.

## Markdown Features

The theme supports the markdown affordances this docs set already uses plus the
features we missed from VitePress:

- `::: tip`, `::: info`, `::: warning`, `::: danger`, and `::: details` blocks.
- `::: code-group` blocks with automatic tabs.
- `::: raw` blocks for trusted HTML passthrough.
- GitHub alert syntax like `> [!NOTE]` and `> [!WARNING]`.
- `[[toc]]` for an inline table of contents generated from page headings.
- `<!-- @include: ./partial.md -->` for markdown includes.
- `<<< ./path/to/file.cs{10-25} [Title]` for code snippets.
- fenced code labels like `csharp [ASP.NET Core]`.
- fenced code line highlights with `{1,3-5}` or inline markers such as
  `[!code highlight]`, `[!code focus]`, `[!code warning]`, `[!code error]`,
  `[!code ++]`, and `[!code --]`.
- `line-numbers`, `line-numbers=42`, and `no-line-numbers` on fences for
  page-level gutter control.
- `<Badge text="Beta" />` or `<Badge type="tip">Stable</Badge>` for small inline
  status labels.
- Mermaid fenced blocks rendered client-side.
- heading anchors and generated page outlines.

Snippet paths are resolved from the current page by default. Use `@/` for the
docs root and `~/` for the repository root.

## Theme Features

`docsTheme()` keeps the reusable theme API grouped around stable site concerns:

- Site identity and deployment: `title`, `description`, `location`, `basePath`,
  `docsRoot`, `brand`, `nav`, `social`, and `footer`.
- Lume integrations: `metas`, `sitemap`, `checkUrls`, and `assets`.
- Documentation chrome: `outline`, `editLink`, `lastUpdated`, `labels`, and
  page-level front matter such as `navbar`, `sidebar`, `aside`, `docFooter`,
  `prev`, and `next`.
- Authoring features: `codeHighlight`, `code`, `snippets`, `markdownMirrors`,
  `llms`, `search`, and `redirects`.
- Escape hatches: `rawPagesDir` and `ignore`.

`lastUpdated` is off by default. Use `lastUpdated: { git: true }` for semantic
last-modified dates from git; this requires `--allow-run=git` in the Deno task.
Use `lastUpdated: true` only when filesystem mtimes are acceptable as a cheap
local fallback.

Some options are project conveniences rather than core theme surface area:
`rawPagesDir`, `llms`, `markdownMirrors`, and `redirects` are useful for this
site and other conversions, but they should stay documented as optional features
instead of assumptions every theme consumer must adopt.

## Generated Artifacts

The build generates:

- `search-index.json` for compact heading-aware client-side search data,
  including keyboard search and recent queries.
- `sitemap.xml` from rendered pages.
- `llms.txt` and `llms-full.txt` for AI-readable documentation.
- markdown mirrors for guide pages, such as `_site/guide/getting-started.md`,
  generated from the markdown captured during preprocessing.
- `404.html` using the docs theme.

## Contributing

When updating the documentation:

1. Keep examples practical and current.
2. Add front matter to new guide pages when you need explicit sidebar order.
3. Use `pages/` for pages that need full HTML control.
4. Run `deno task build` before opening a PR.
