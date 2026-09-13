import { access, readFile } from "node:fs/promises";
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
  }

  console.log(`${label} -> /${identityEndpoint.Route}`);
}

console.log("Published static-asset verification passed.");
