import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { mkdir, readFile, rename, stat, unlink, writeFile } from "node:fs/promises";
import { dirname, extname, isAbsolute, join, resolve } from "node:path";
import { argv } from "node:process";
import sharp from "sharp";
import type { OutputInfo } from "sharp";

const TARGET_WIDTHS = [640, 1280, 2048] as const;
const SOURCE_FORMATS = new Set(["heif", "jpeg", "png", "tiff", "webp"]);
const ID_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const HASHED_FILE_PATTERN = /^([a-z0-9]+(?:-[a-z0-9]+)*)\.([0-9a-f]{12})\.(\d+)\.(jpe?g|webp)$/;

type SourceEntry = {
  id: string;
  sourcePath: string;
  altText: string;
  caption?: string | null;
  publishedAt: string;
  order: number;
};

type SourceCatalog = {
  schemaVersion: number;
  entries: SourceEntry[];
};

type PhotoVariant = {
  url: string;
  width: number;
  height: number;
  mediaType: "image/jpeg" | "image/webp";
};

type PhotoEntry = {
  id: string;
  publishedAt: string;
  altText: string;
  caption?: string;
  variants: PhotoVariant[];
};

type PhotoManifest = {
  schemaVersion: 1;
  contentRevision: string;
  publishedAt: string;
  entries: PhotoEntry[];
};

type CliOptions = {
  catalogPath: string;
  outputRoot: string;
};

/** Builds and verifies one atomic publication package from an authored source catalog. */
async function main(): Promise<void> {
  const options = parseArguments(argv.slice(2));
  const sourceCatalog = await loadSourceCatalog(options.catalogPath);
  const publicPhotoDirectory = join(options.outputRoot, "public", "photos");
  const catalogDirectory = join(options.outputRoot, "catalog");

  await Promise.all([mkdir(publicPhotoDirectory, { recursive: true }), mkdir(catalogDirectory, { recursive: true })]);

  const entries: PhotoEntry[] = [];
  const sourceHashes = new Map<string, string>();

  for (const source of [...sourceCatalog.entries].sort((left, right) => left.order - right.order)) {
    const sourcePath = resolveSourcePath(options.catalogPath, source.sourcePath);
    await validateSourceFile(sourcePath);

    const sourceHash = await hashFile(sourcePath);
    const duplicateId = sourceHashes.get(sourceHash);
    if (duplicateId !== undefined) {
      throw new Error(`Photos '${duplicateId}' and '${source.id}' use duplicate source content.`);
    }
    sourceHashes.set(sourceHash, source.id);

    entries.push(await buildEntry(source, sourcePath, publicPhotoDirectory));
  }

  const revisionHash = sha256(JSON.stringify(entries)).slice(0, 16);
  const previousManifest = await readExistingManifest(join(catalogDirectory, "photos.v1.json"));
  const manifest: PhotoManifest = {
    schemaVersion: 1,
    contentRevision: `photos-${revisionHash}`,
    publishedAt: previousManifest?.contentRevision === `photos-${revisionHash}`
      ? previousManifest.publishedAt
      : new Date().toISOString(),
    entries,
  };

  await verifyPublication(manifest, options.outputRoot);
  await writeManifestAtomically(manifest, catalogDirectory);
  await writeChecksums(manifest, options.outputRoot);

  process.stdout.write(
    `Built ${manifest.entries.length} photo(s), ${manifest.entries.flatMap((entry) => entry.variants).length} derivative(s), revision ${manifest.contentRevision}.\n`,
  );
}

/** Parses the required catalog and output arguments without accepting ambiguous positional input. */
function parseArguments(arguments_: string[]): CliOptions {
  if (arguments_.includes("--help")) {
    process.stdout.write("Usage: npm run photos:build -- --catalog <source.json> --output <publication-root>\n");
    process.exit(0);
  }

  const values = new Map<string, string>();
  for (let index = 0; index < arguments_.length; index += 2) {
    const name = arguments_[index];
    const value = arguments_[index + 1];
    if ((name !== "--catalog" && name !== "--output") || value === undefined || value.startsWith("--")) {
      throw new Error("Expected --catalog <source.json> and --output <publication-root>.");
    }
    values.set(name, value);
  }

  const catalogPath = values.get("--catalog");
  const outputRoot = values.get("--output");
  if (catalogPath === undefined || outputRoot === undefined || values.size !== 2) {
    throw new Error("Both --catalog and --output are required.");
  }

  return { catalogPath: resolve(catalogPath), outputRoot: resolve(outputRoot) };
}

/** Loads strict authored metadata before any image processing begins. */
async function loadSourceCatalog(path: string): Promise<SourceCatalog> {
  const value: unknown = JSON.parse(await readFile(path, "utf8"));
  assertObject(value, "Source catalog");
  assertExactKeys(value, ["schemaVersion", "entries"], "Source catalog");

  if (value.schemaVersion !== 1 || !Array.isArray(value.entries)) {
    throw new Error("Source catalog requires schemaVersion 1 and an entries array.");
  }

  const ids = new Set<string>();
  const orders = new Set<number>();
  const entries = value.entries.map((entry, index) => validateSourceEntry(entry, index, ids, orders));
  return { schemaVersion: 1, entries };
}

/** Validates one authored record and its stable ordering fields. */
function validateSourceEntry(
  value: unknown,
  index: number,
  ids: Set<string>,
  orders: Set<number>,
): SourceEntry {
  const label = `Source entry ${index + 1}`;
  assertObject(value, label);
  assertExactKeys(value, ["id", "sourcePath", "altText", "caption", "publishedAt", "order"], label, ["caption"]);

  if (typeof value.id !== "string" || !ID_PATTERN.test(value.id) || ids.has(value.id)) {
    throw new Error(`${label} has an invalid or duplicate id.`);
  }
  if (typeof value.sourcePath !== "string" || value.sourcePath.trim().length === 0) {
    throw new Error(`${label} requires sourcePath.`);
  }
  if (typeof value.altText !== "string" || value.altText.trim().length === 0 || value.altText.length > 300) {
    throw new Error(`${label} altText must contain 1 to 300 characters.`);
  }
  if (value.caption !== undefined && value.caption !== null
      && (typeof value.caption !== "string" || value.caption.length > 500)) {
    throw new Error(`${label} caption must be null or contain at most 500 characters.`);
  }
  if (typeof value.publishedAt !== "string" || !isIsoDate(value.publishedAt)) {
    throw new Error(`${label} requires an ISO 8601 publishedAt value.`);
  }
  if (!Number.isSafeInteger(value.order) || (value.order as number) < 0 || orders.has(value.order as number)) {
    throw new Error(`${label} has an invalid or duplicate order.`);
  }

  ids.add(value.id);
  orders.add(value.order as number);
  return value as SourceEntry;
}

/** Produces responsive JPEG and WebP variants while removing source metadata. */
async function buildEntry(
  source: SourceEntry,
  sourcePath: string,
  outputDirectory: string,
): Promise<PhotoEntry> {
  const metadata = await sharp(sourcePath, { failOn: "error", limitInputPixels: 200_000_000 }).metadata();
  if (metadata.width === undefined || metadata.height === undefined || metadata.format === undefined) {
    throw new Error(`Photo '${source.id}' has unreadable dimensions or format.`);
  }
  if (!SOURCE_FORMATS.has(metadata.format) || (metadata.pages ?? 1) !== 1) {
    throw new Error(`Photo '${source.id}' uses unsupported format '${metadata.format}' or has multiple pages.`);
  }

  const swapsAxes = metadata.orientation !== undefined && metadata.orientation >= 5 && metadata.orientation <= 8;
  const orientedWidth = swapsAxes ? metadata.height : metadata.width;
  const widths = selectWidths(orientedWidth);
  const variants: PhotoVariant[] = [];

  for (const width of widths) {
    const jpeg = await sharp(sourcePath, { failOn: "error", limitInputPixels: 200_000_000 })
      .autoOrient()
      .resize({ width, withoutEnlargement: true })
      .toColourspace("srgb")
      .jpeg({ quality: 85, mozjpeg: true })
      .toBuffer({ resolveWithObject: true });
    variants.push(await writeVariant(source.id, width, "jpg", "image/jpeg", jpeg, outputDirectory));

    const webp = await sharp(sourcePath, { failOn: "error", limitInputPixels: 200_000_000 })
      .autoOrient()
      .resize({ width, withoutEnlargement: true })
      .toColourspace("srgb")
      .webp({ quality: 82 })
      .toBuffer({ resolveWithObject: true });
    variants.push(await writeVariant(source.id, width, "webp", "image/webp", webp, outputDirectory));
  }

  return {
    id: source.id,
    publishedAt: new Date(source.publishedAt).toISOString(),
    altText: source.altText,
    ...(source.caption === undefined || source.caption === null ? {} : { caption: source.caption }),
    variants,
  };
}

/** Selects configured widths without upscaling and retains a useful intrinsic-size fallback. */
function selectWidths(orientedWidth: number): number[] {
  const widths = TARGET_WIDTHS.filter((width) => width <= orientedWidth);
  const cappedIntrinsicWidth = Math.min(orientedWidth, TARGET_WIDTHS.at(-1) as number);
  if (widths.length === 0 || widths.at(-1) !== cappedIntrinsicWidth) {
    return [...widths, cappedIntrinsicWidth];
  }
  return widths;
}

/** Writes a derivative only when its content-addressed filename is not already reusable. */
async function writeVariant(
  id: string,
  requestedWidth: number,
  extension: "jpg" | "webp",
  mediaType: "image/jpeg" | "image/webp",
  output: { data: Buffer; info: OutputInfo },
  directory: string,
): Promise<PhotoVariant> {
  if (output.info.width !== requestedWidth || output.info.height <= 0) {
    throw new Error(`Photo '${id}' did not produce the requested ${requestedWidth}px derivative.`);
  }

  const hash = sha256(output.data).slice(0, 12);
  const filename = `${id}.${hash}.${output.info.width}.${extension}`;
  const path = join(directory, filename);

  try {
    await writeFile(path, output.data, { flag: "wx", mode: 0o644 });
  } catch (error) {
    if (!isAlreadyExists(error) || await hashFile(path) !== sha256(output.data)) {
      throw error;
    }
  }

  return {
    url: `/media/photos/${filename}`,
    width: output.info.width,
    height: output.info.height,
    mediaType,
  };
}

/** Verifies manifest paths, dimensions, hashes, and generated files before activation. */
async function verifyPublication(manifest: PhotoManifest, outputRoot: string): Promise<void> {
  const seenUrls = new Set<string>();
  for (const entry of manifest.entries) {
    for (const variant of entry.variants) {
      const filename = variant.url.slice("/media/photos/".length);
      const match = HASHED_FILE_PATTERN.exec(filename);
      if (match === null || match[1] !== entry.id || Number(match[3]) !== variant.width || seenUrls.has(variant.url)) {
        throw new Error(`Generated variant '${variant.url}' failed manifest validation.`);
      }

      const path = join(outputRoot, "public", "photos", filename);
      const metadata = await sharp(path).metadata();
      if (metadata.width !== variant.width
          || metadata.height !== variant.height
          || `image/${metadata.format}` !== variant.mediaType
          || await hashFile(path).then((hash) => hash.slice(0, 12)) !== match[2]) {
        throw new Error(`Generated variant '${variant.url}' failed file verification.`);
      }
      seenUrls.add(variant.url);
    }
  }
}

/** Atomically replaces the active local manifest after every referenced derivative verifies. */
async function writeManifestAtomically(manifest: PhotoManifest, directory: string): Promise<void> {
  const destination = join(directory, "photos.v1.json");
  const temporary = join(directory, `.photos.v1.${process.pid}.${Date.now()}.tmp`);
  await writeFile(temporary, `${JSON.stringify(manifest, null, 2)}\n`, { flag: "wx", mode: 0o644 });

  try {
    await rename(temporary, destination);
  } catch (error) {
    await unlink(temporary).catch(() => undefined);
    throw error;
  }
}

/** Writes remote-transfer checksums for the active manifest and all referenced derivatives. */
async function writeChecksums(manifest: PhotoManifest, outputRoot: string): Promise<void> {
  const relativePaths = [
    "catalog/photos.v1.json",
    ...manifest.entries.flatMap((entry) => entry.variants.map((variant) => `public${variant.url.slice("/media".length)}`)),
  ];
  const lines: string[] = [];
  for (const relativePath of relativePaths) {
    lines.push(`${await hashFile(join(outputRoot, relativePath))}  ${relativePath}`);
  }
  await writeFile(join(outputRoot, "checksums.sha256"), `${lines.join("\n")}\n`, { mode: 0o644 });
}

/** Resolves relative source paths beside the private authored catalog. */
function resolveSourcePath(catalogPath: string, sourcePath: string): string {
  return isAbsolute(sourcePath) ? sourcePath : resolve(dirname(catalogPath), sourcePath);
}

/** Rejects missing, non-regular, or extensionless source files before decoding. */
async function validateSourceFile(path: string): Promise<void> {
  const file = await stat(path);
  if (!file.isFile() || extname(path).length === 0) {
    throw new Error(`Source '${path}' must be a regular image file with an extension.`);
  }
}

/** Reads a prior manifest only to preserve publication time for identical content. */
async function readExistingManifest(path: string): Promise<PhotoManifest | undefined> {
  try {
    return JSON.parse(await readFile(path, "utf8")) as PhotoManifest;
  } catch (error) {
    if (isMissing(error) || error instanceof SyntaxError) {
      return undefined;
    }
    throw error;
  }
}

/** Computes a streaming SHA-256 digest without loading an original into memory. */
async function hashFile(path: string): Promise<string> {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) {
    hash.update(chunk);
  }
  return hash.digest("hex");
}

/** Computes a SHA-256 digest for generated bytes or canonical text. */
function sha256(value: Buffer | string): string {
  return createHash("sha256").update(value).digest("hex");
}

/** Requires an object before strict key and field validation. */
function assertObject(value: unknown, label: string): asserts value is Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }
}

/** Rejects unknown or missing keys so catalog typos cannot silently publish. */
function assertExactKeys(
  value: Record<string, unknown>,
  allowed: string[],
  label: string,
  optional: string[] = [],
): void {
  const allowedKeys = new Set(allowed);
  const unknown = Object.keys(value).filter((key) => !allowedKeys.has(key));
  const missing = allowed.filter((key) => !optional.includes(key) && !(key in value));
  if (unknown.length > 0 || missing.length > 0) {
    throw new Error(`${label} has unknown keys [${unknown.join(", ")}] or missing keys [${missing.join(", ")}].`);
  }
}

/** Accepts only date strings that parse as explicit ISO 8601 instants. */
function isIsoDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$/.test(value)
    && !Number.isNaN(Date.parse(value));
}

/** Identifies an expected exclusive-create collision for reusable derivatives. */
function isAlreadyExists(error: unknown): error is NodeJS.ErrnoException {
  return error instanceof Error && "code" in error && error.code === "EEXIST";
}

/** Identifies an absent prior manifest without masking malformed existing data. */
function isMissing(error: unknown): error is NodeJS.ErrnoException {
  return error instanceof Error && "code" in error && error.code === "ENOENT";
}

main().catch((error: unknown) => {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
});
