import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const require = createRequire(import.meta.url);
const { parse } = require(process.env.QSOCK_BABEL_PARSER || '@babel/parser');
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const source = resolve(process.argv[2] || join(repository, 'artifacts/material-icons'));
const version = '9.5.0';
const integrity = 'sha512-dahtVaqnRUIBgaZ4WC9EdP+LgJ5d2rslASYIX+nDFG6+ssUiM11jQ+Gd1wH8vhUGoNA9ES40MYics5BI8zJFXQ==';
const archive = await readFile(join(source, `icons-material-${version}.tgz`));
const actualIntegrity = 'sha512-' + createHash('sha512').update(archive).digest('base64');
if (actualIntegrity !== integrity) throw new Error('MUI archive integrity mismatch');

const packageRoot = join(source, 'package');
const metadata = JSON.parse(await readFile(join(packageRoot, 'package.json'), 'utf8'));
if (metadata.name !== '@mui/icons-material' || metadata.version !== version) {
  throw new Error('Unexpected MUI package');
}

function properties(node) {
  if (node.type !== 'ObjectExpression') throw new Error(`Unsupported properties: ${node.type}`);
  return Object.fromEntries(node.properties.map(property => {
    if (property.type !== 'ObjectProperty' || property.computed) {
      throw new Error(`Unsupported property: ${property.type}`);
    }
    return [property.key.name || property.key.value, property.value];
  }));
}

function literal(node) {
  if (!['StringLiteral', 'NumericLiteral'].includes(node.type)) {
    throw new Error(`Unsupported value: ${node.type}`);
  }
  return node.value;
}

function decode(node) {
  if (node.type === 'ArrayExpression') return node.elements.flatMap(decode);
  if (node.type !== 'CallExpression' || !['_jsx', '_jsxs'].includes(node.callee.name)) {
    throw new Error(`Unsupported icon expression: ${node.type}`);
  }
  const [tag, attributes] = node.arguments;
  const type = tag.type === 'StringLiteral'
    ? tag.value
    : tag.type === 'MemberExpression' && tag.object.name === 'React' && tag.property.name === 'Fragment'
      ? 'g'
      : null;
  if (!['path', 'circle', 'ellipse', 'g', 'defs', 'use'].includes(type)) {
    throw new Error(`Unsupported SVG element: ${type}`);
  }
  const values = properties(attributes);
  const children = values.children ? decode(values.children) : [];
  delete values.children;
  const props = Object.fromEntries(Object.entries(values).map(([key, value]) => [key, literal(value)]));
  return [{ type, props, children }];
}

function number(value) {
  const result = Number(value);
  if (!Number.isFinite(result)) throw new Error(`Invalid SVG number: ${value}`);
  return result;
}

function convert(nodes) {
  const definitions = new Map();
  function collect(items) {
    for (const node of items) {
      if (node.props.id) definitions.set(node.props.id, node);
      collect(node.children);
    }
  }
  collect(nodes);

  function visit(node, inherited = { fillRule: 'nonzero', fillOpacity: 1 }, references = new Set()) {
    if (node.type === 'defs') return null;
    const allowed = {
      path: ['d', 'id', 'opacity', 'fillOpacity', 'fillRule', 'transform'],
      circle: ['cx', 'cy', 'r', 'opacity', 'fillOpacity', 'fillRule'],
      ellipse: ['cx', 'cy', 'rx', 'ry', 'opacity', 'fillOpacity', 'fillRule'],
      g: ['opacity', 'fillOpacity', 'fillRule'],
      use: ['xlinkHref', 'overflow'],
    }[node.type];
    for (const key of Object.keys(node.props)) {
      if (!allowed.includes(key)) throw new Error(`Unsupported SVG attribute: ${node.type}.${key}`);
    }
    if (node.type === 'use') {
      const reference = node.props.xlinkHref;
      if (!reference.startsWith('#') || references.has(reference)) throw new Error('Invalid SVG reference');
      if (node.props.overflow && node.props.overflow !== 'visible') throw new Error('Unsupported SVG clipping');
      const target = definitions.get(reference.slice(1));
      if (!target) throw new Error(`Missing SVG definition: ${reference}`);
      return visit(target, inherited, new Set([...references, reference]));
    }
    const fillRule = node.props.fillRule || inherited.fillRule;
    const fillOpacity = node.props.fillOpacity === undefined ? inherited.fillOpacity : number(node.props.fillOpacity);
    if (!['nonzero', 'evenodd'].includes(fillRule) || fillOpacity < 0 || fillOpacity > 1) {
      throw new Error('Unsupported SVG fill');
    }
    const result = { type: node.type };
    if (node.props.opacity !== undefined) {
      result.opacity = number(node.props.opacity);
      if (result.opacity < 0 || result.opacity > 1) throw new Error('Invalid SVG opacity');
    }
    if (node.type === 'g') {
      result.children = node.children.map(child => visit(child, { fillRule, fillOpacity }, references)).filter(Boolean);
      return result;
    }
    if (fillRule !== 'nonzero') result.fillRule = fillRule;
    if (fillOpacity !== 1) result.fillOpacity = fillOpacity;
    if (node.type === 'path') result.data = node.props.d;
    else {
      for (const key of node.type === 'circle' ? ['cx', 'cy', 'r'] : ['cx', 'cy', 'rx', 'ry']) {
        result[key] = number(node.props[key]);
      }
    }
    if (node.props.transform) {
      const match = /^scale\(\s*([\d.]+)\s*,\s*([\d.]+)\s*\)$/.exec(node.props.transform);
      if (!match) throw new Error(`Unsupported SVG transform: ${node.props.transform}`);
      result.scale = [number(match[1]), number(match[2])];
    }
    return result;
  }
  return nodes.map(node => visit(node)).filter(Boolean);
}

const files = (await readdir(packageRoot)).filter(file => file.endsWith('.mjs') && file !== 'index.mjs').sort();
const icons = {};
const counts = { Filled: 0, Outlined: 0, Rounded: 0, Sharp: 0, TwoTone: 0 };
const normalizedNames = new Set();
for (const file of files) {
  try {
    const ast = parse(await readFile(join(packageRoot, file), 'utf8'), { sourceType: 'module' });
    const declaration = ast.program.body.find(node => node.type === 'ExportDefaultDeclaration')?.declaration;
    if (declaration?.type !== 'CallExpression' || declaration.callee.name !== 'createSvgIcon' || declaration.arguments.length !== 2) {
      throw new Error('Expected createSvgIcon export');
    }
    const name = literal(declaration.arguments[1]);
    if (file !== `${name}.mjs`) throw new Error('Icon name does not match module');
    const normalized = name.toLowerCase();
    if (normalizedNames.has(normalized)) throw new Error(`Ambiguous icon name: ${name}`);
    normalizedNames.add(normalized);
    icons[name] = convert(decode(declaration.arguments[0]));
    if (icons[name].length === 0) throw new Error('Empty icon');
    const variant = Object.keys(counts).find(value => value !== 'Filled' && name.endsWith(value)) || 'Filled';
    counts[variant]++;
  } catch (error) {
    throw new Error(`${file}: ${error.message}`, { cause: error });
  }
}
const index = await readFile(join(packageRoot, 'index.mjs'), 'utf8');
const exports = [...index.matchAll(/export \{ default as (\w+) \} from/g)].map(match => match[1]).sort();
if (JSON.stringify(exports) !== JSON.stringify(Object.keys(icons))) throw new Error('Incomplete MUI catalog');

const output = JSON.stringify(icons) + '\n';
const compressed = gzipSync(output, { level: 9 });
await writeFile(join(repository, 'Q.Sock.UI/Resources/material-icons.json.gz'), compressed);
await writeFile(join(repository, 'licenses/MUI-icons.txt'), await readFile(join(packageRoot, 'LICENSE')));
await writeFile(join(repository, 'tools/icons/material-icons-source.json'), JSON.stringify({
  package: metadata.name,
  version,
  tarball: `https://registry.npmjs.org/@mui/icons-material/-/icons-material-${version}.tgz`,
  integrity,
  count: files.length,
  variants: counts,
  catalogSha256: createHash('sha256').update(output).digest('hex'),
  compressedBytes: compressed.length,
}, null, 2) + '\n');
console.log(JSON.stringify({ icons: files.length, variants: counts, bytes: Buffer.byteLength(output), compressedBytes: compressed.length }, null, 2));
