import * as esbuild from "esbuild";
import { readFile, rm } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const production = process.argv.includes("--production");
const watch = process.argv.includes("--watch");
const webRoot = "src/Portfolio.Web/wwwroot";

/**
 * Each entry is emitted as one self-contained file with a stable name; MapStaticAssets adds the fingerprint.
 * Code splitting stays off because shared chunks would be imported by unfingerprinted relative URLs.
 */
const entries = [
    // Runs as a classic head script before the stylesheet paints, so it cannot be a module.
    { in: "src/Portfolio.Web/Client/theme.ts", out: "assets/js/theme", format: "iife" },
    { in: "src/Portfolio.Web/Client/navigation.ts", out: "assets/js/navigation", format: "esm" },
    // Imported only from the photos view by its fingerprinted URL; its export name must survive minification.
    { in: "src/Portfolio.Web/Client/photos.ts", out: "assets/js/photos", format: "esm" },
    // The @import list is inlined here, so the browser receives one stylesheet.
    { in: "src/Portfolio.Web/Styles/portfolio.css", out: "css/portfolio", format: undefined }
];

// Explicit browser floors keep esbuild from lowering modern syntax such as light-dark() or @starting-style.
const target = ["es2022", "chrome123", "edge123", "firefox120", "safari17.5"];

const shared = {
    bundle: true,
    splitting: false,
    target,
    outdir: webRoot,
    charset: "utf8",
    sourcemap: false,
    legalComments: "none",
    // Stylesheet fonts are copied with content-hashed names and referenced by absolute URL.
    loader: { ".woff2": "file" },
    assetNames: "assets/fonts/[name]-[hash]",
    publicPath: "/",
    minify: production,
    logLevel: "warning"
};

const contexts = await Promise.all(entries.map((entry) => esbuild.context({
    ...shared,
    entryPoints: [{ in: entry.in, out: entry.out }],
    ...(entry.format ? { format: entry.format } : {})
})));

/** Fails the build when output could reintroduce a request waterfall, a source map, or a comment. */
async function verifyOutput() {
    const css = await readFile(path.join(webRoot, "css/portfolio.css"), "utf8");
    if (/@import\b/.test(css)) {
        throw new Error("css/portfolio.css still contains @import; every stylesheet must be inlined.");
    }

    if (!production) {
        return;
    }

    for (const file of ["css/portfolio.css", ...entries.filter((e) => e.format).map((e) => `${e.out}.js`)]) {
        const content = await readFile(path.join(webRoot, file), "utf8");
        if (content.includes("/*") || content.includes("sourceMappingURL")) {
            throw new Error(`${file} contains a comment or source map reference after production minification.`);
        }
    }
}

async function build() {
    // Hashed font names change with content; clear stale copies so only referenced fonts are published.
    await rm(path.join(webRoot, "assets/fonts"), { recursive: true, force: true });
    await Promise.all(contexts.map((context) => context.rebuild()));
    await verifyOutput();
    console.log(`Built ${entries.length} client assets (${production ? "production: bundled, minified, comments removed" : "development"}).`);
}

if (watch) {
    if (production) {
        throw new Error("--watch is for development builds only.");
    }

    await build();
    await Promise.all(contexts.map((context) => context.watch()));
    console.log("Watching src/Portfolio.Web/Client and src/Portfolio.Web/Styles for changes...");
} else {
    try {
        await build();
    } catch (error) {
        console.error(error instanceof Error ? error.message : error);
        process.exitCode = 1;
    } finally {
        await Promise.all(contexts.map((context) => context.dispose()));
    }
}
