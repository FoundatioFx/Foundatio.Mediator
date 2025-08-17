import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Foundatio.Mediator',
  description: 'Blazingly fast, convention-based C# mediator powered by source generators and interceptors',
  base: '/',
  head: [
    ['link', { rel: 'icon', href: '/favicon.ico' }],
    ['meta', { name: 'theme-color', content: '#3c8772' }]
  ],
  themeConfig: {
    logo: {
      light: 'https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg',
      dark: 'https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg'
    },
    siteTitle: 'Mediator',
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Examples', link: '/examples/simple-handlers' },
      { text: 'API Reference', link: '/api/mediator' },
      { text: 'GitHub', link: 'https://github.com/FoundatioFx/Foundatio.Mediator' }
    ],
    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'What is Foundatio.Mediator?', link: '/guide/what-is-foundatio-mediator' },
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Why Choose Foundatio.Mediator?', link: '/guide/why-foundatio-mediator' }
          ]
        },
        {
          text: 'Core Concepts',
          items: [
            { text: 'Handler Conventions', link: '/guide/handler-conventions' },
            { text: 'Dependency Injection', link: '/guide/dependency-injection' },
            { text: 'Result Types', link: '/guide/result-types' },
            { text: 'Middleware', link: '/guide/middleware' }
          ]
        },
        {
          text: 'Advanced Topics',
          items: [
            { text: 'Cascading Messages', link: '/guide/cascading-messages' },
            { text: 'Streaming Handlers', link: '/guide/streaming-handlers' },
            { text: 'Performance & Interceptors', link: '/guide/performance' },
            { text: 'Configuration Options', link: '/guide/configuration' }
          ]
        }
      ],
      '/examples/': [
        {
          text: 'Basic Examples',
          items: [
            { text: 'Simple Handlers', link: '/examples/simple-handlers' },
            { text: 'CRUD Operations', link: '/examples/crud-operations' },
            { text: 'Event Publishing', link: '/examples/event-publishing' }
          ]
        },
        {
          text: 'Advanced Examples',
          items: [
            { text: 'Validation Middleware', link: '/examples/validation-middleware' },
            { text: 'Logging Middleware', link: '/examples/logging-middleware' },
            { text: 'Streaming Data', link: '/examples/streaming-data' }
          ]
        }
      ],
      '/api/': [
        {
          text: 'API Reference',
          items: [
            { text: 'IMediator Interface', link: '/api/mediator' },
            { text: 'Result Types', link: '/api/result-types' },
            { text: 'Attributes', link: '/api/attributes' },
            { text: 'Configuration', link: '/api/configuration' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/FoundatioFx/Foundatio.Mediator' },
      { icon: 'discord', link: 'https://discord.gg/6HxgFCx' }
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024 Foundatio'
    },
    editLink: {
      pattern: 'https://github.com/FoundatioFx/Foundatio.Mediator/edit/main/docs/:path'
    },
    search: {
      provider: 'local'
    }
  },
  markdown: {
    lineNumbers: false,
    codeTransformers: [
      {
        // Use VitePress snippets feature to reference real code files
        name: 'snippet-transformer',
        preprocess(code, options) {
          return code
        }
      }
    ]
  }
})
