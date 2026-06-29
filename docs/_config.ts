import lume from "lume/mod.ts";
import docsTheme from "./_theme/mod.ts";

const location = new URL("https://mediator.foundatio.dev");
const site = lume({
    location,
    prettyUrls: true,
    dest: "_site",
});

site.use(docsTheme({
    title: "Foundatio Mediator",
    description:
        "Blazingly fast, convention-based C# mediator powered by source generators and interceptors",
    location,
    brand: {
        label: "Mediator",
        logoLight:
            "https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg",
        logoDark:
            "https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg",
        icon:
            "https://raw.githubusercontent.com/FoundatioFx/Foundatio/main/media/foundatio-icon.png",
        themeColor: "#3c8772",
    },
    docsRoot: "guide",
    nav: [
        {
            text: "Guide",
            link: "/guide/what-is-foundatio-mediator",
            activeMatch: "^/guide/",
        },
        {
            text: "GitHub",
            link: "https://github.com/FoundatioFx/Foundatio.Mediator",
        },
    ],
    social: [
        {
            label: "GitHub",
            link: "https://github.com/FoundatioFx/Foundatio.Mediator",
            class: "github",
        },
        {
            label: "Discord",
            link: "https://discord.gg/6HxgFCx",
            class: "discord",
        },
    ],
    footer: {
        message: "Released under the MIT License.",
        copyright: "Copyright © 2026 Foundatio",
    },
    editLink: {
        pattern:
            "https://github.com/FoundatioFx/Foundatio.Mediator/edit/main/docs/:path",
    },
    lastUpdated: false,
    llms: true,
    markdownMirrors: true,
}));

site.copy("public", ".");

export default site;
