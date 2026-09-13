import { access, readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const publishDirectory = path.resolve(process.argv[2] ?? "artifacts/publish");
const manifestPath = path.join(
  publishDirectory,
  "Portfolio.Web.staticwebassets.endpoints.json",
);

const expectedAssets = new Map([
  ["css/portfolio.css", "text/css"],
  ["assets/js/theme.js", "text/javascript"],
  ["assets/js/navigation.js", "text/javascript"],
  ["assets/js/photos.js", "text/javascript"],
  ["assets/svg/bt-logo-boxed.svg", "image/svg+xml"],
  ["assets/svg/external-link-nobox.svg", "image/svg+xml"],
]);

function properties(endpoint) {
  return new Map(
    endpoint.EndpointProperties.map(({ Name, Value }) => [Name, Value]),
  );
}

function headers(endpoint) {
  return new Map(endpoint.ResponseHeaders.map(({ Name, Value }) => [Name, Value]));
}

function fail(message) {
  throw new Error(`Published static-asset verification failed: ${message}`);
}

const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const publishedAssets = new Map();

if (manifest.ManifestType !== "Publish") {
  fail(`${manifestPath} is not a publish manifest.`);
}

for (const [label, expectedContentType] of expectedAssets) {
  const labelledEndpoints = manifest.Endpoints.filter(
    (endpoint) => properties(endpoint).get("label") === label,
  );
  const identityEndpoint = labelledEndpoints.find(
    (endpoint) => endpoint.Selectors.length === 0,
  );

  if (!identityEndpoint) {
    fail(`${label} has no labelled identity endpoint.`);
  }

  const fingerprintedRoutes = new Set(
    labelledEndpoints.map((endpoint) => endpoint.Route),
  );
  if (fingerprintedRoutes.size !== 1) {
    fail(`${label} exposes more than one fingerprinted release route.`);
  }

  const fingerprint = properties(identityEndpoint).get("fingerprint");
  if (!fingerprint || identityEndpoint.Route === label) {
    fail(`${label} does not use a fingerprinted route.`);
  }

  const identityHeaders = headers(identityEndpoint);
  if (identityHeaders.get("Cache-Control") !== "max-age=31536000, immutable") {
    fail(`${identityEndpoint.Route} is not one-year immutable.`);
  }

  if (identityHeaders.get("Content-Type") !== expectedContentType) {
    fail(`${identityEndpoint.Route} has an unexpected content type.`);
  }

  await access(path.join(publishDirectory, "wwwroot", identityEndpoint.AssetFile));
  const publishedAsset = { identity: path.join(publishDirectory, "wwwroot", identityEndpoint.AssetFile) };
  publishedAssets.set(label, publishedAsset);

  for (const encoding of ["br", "gzip"]) {
    const encodedEndpoint = labelledEndpoints.find((endpoint) =>
      endpoint.Selectors.some(
        (selector) =>
          selector.Name === "Content-Encoding" && selector.Value === encoding,
      ),
    );

    if (!encodedEndpoint || encodedEndpoint.Route !== identityEndpoint.Route) {
      fail(`${identityEndpoint.Route} has no negotiated ${encoding} representation.`);
    }

    const encodedHeaders = headers(encodedEndpoint);
    if (
      encodedHeaders.get("Cache-Control") !== "max-age=31536000, immutable" ||
      encodedHeaders.get("Content-Encoding") !== encoding ||
      encodedHeaders.get("Content-Type") !== expectedContentType
    ) {
      fail(`${identityEndpoint.Route} has invalid ${encoding} response metadata.`);
    }

    await access(path.join(publishDirectory, "wwwroot", encodedEndpoint.AssetFile));
    publishedAsset[encoding] = path.join(publishDirectory, "wwwroot", encodedEndpoint.AssetFile);
  }

  console.log(`${label} -> /${identityEndpoint.Route}`);
}

async function* walk(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      yield* walk(fullPath);
    } else {
      yield fullPath;
    }
  }
}

// Client source and source maps stay out of every published tree.
for await (const file of walk(publishDirectory)) {
  const relativePath = path.relative(publishDirectory, file);
  if (/\.(?:ts|map)$/i.test(file) || relativePath.split(path.sep).includes("Styles")) {
    fail(`${relativePath} is client source or a source map.`);
  }
}

// Production client assets are minified with every comment removed.
for (const label of ["css/portfolio.css", "assets/js/theme.js", "assets/js/navigation.js", "assets/js/photos.js"]) {
  const content = await readFile(publishedAssets.get(label).identity, "utf8");
  if (content.includes("/*") || content.includes("sourceMappingURL") || /\n[ \t]+\S/.test(content)) {
    fail(`${label} is not a minified production build.`);
  }
}

// Budgets apply to the Brotli representations browsers negotiate for the initial document.
const brotliBytes = async (labels) =>
  (await Promise.all(labels.map((label) => stat(publishedAssets.get(label).br)))).reduce((total, file) => total + file.size, 0);
const budgets = [
  { name: "initial JavaScript", labels: ["assets/js/theme.js", "assets/js/navigation.js"], limit: 10 * 1024 },
  { name: "stylesheet", labels: ["css/portfolio.css"], limit: 20 * 1024 },
  { name: "photos view JavaScript (lazy, reported only)", labels: ["assets/js/photos.js"], limit: Infinity }
];
for (const budget of budgets) {
  const bytes = await brotliBytes(budget.labels);
  const limit = Number.isFinite(budget.limit) ? ` / ${budget.limit} B` : "";
  console.log(`${budget.name}: ${bytes} B Brotli${limit}`);
  if (bytes > budget.limit) {
    fail(`${budget.name} exceeds its ${budget.limit} B Brotli budget.`);
  }
}

console.log("Published static-asset verification passed.");
