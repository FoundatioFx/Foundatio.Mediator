# Foundatio.Mediator Documentation

This folder contains the VitePress v2.0 documentation for Foundatio.Mediator.

## Development

To run the documentation site locally:

```bash
cd docs
npm install
npm run dev
```

The documentation will be available at http://localhost:5173/

## Building

To build the documentation for production:

```bash
npm run build
```

The built site will be in the `docs/.vitepress/dist` directory.

## Code Snippets

The documentation uses VitePress code snippets to reference real code from the repository:

```markdown
@[code{10-20}](../../samples/ConsoleSample/Handlers/Handlers.cs)
```

This ensures code examples stay up-to-date with the actual implementation.

## Configuration

VitePress configuration is in `.vitepress/config.ts` with:

- Navigation structure
- Theme customization
- Search configuration
- Build optimization
- Snippet handling

## Contributing

When updating the documentation:

1. **Keep code snippets current** - Use the snippet feature to reference real code
2. **Test locally** - Always run `npm run dev` to verify changes
3. **Check navigation** - Ensure new pages are added to the sidebar
4. **Validate links** - Check internal links work correctly
5. **Update examples** - Keep examples practical and realistic

## Writing Guidelines

- Use clear, concise language
- Include practical examples for every concept
- Reference real code from the samples when possible
- Provide both basic and advanced usage patterns
- Include performance considerations where relevant
- Add "Next Steps" sections to guide readers
