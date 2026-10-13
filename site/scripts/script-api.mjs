// Builds the script reference: has the engine write its script API as JSON,
// then turns that into Starlight pages under src/content/docs/reference/script.
// Handed a path, it reads that file and does not ask the engine:
//   node scripts/script-api.mjs path/to/script-api.json
import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// The shape of the JSON this reads. The engine's is ScriptApiJson.Format.
const FORMAT = 1;

const site = join(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(site, 'src', 'content', 'docs', 'reference', 'script');
const BASE = '/reference/script';

// Where the site says what the classes of a group do. A group that is not
// here, as one of a project's own, gets no link.
const ENTITY_PAGES = {
	Logic: '/reference/logic-entities/',
	Movers: '/reference/mover-entities/',
	Player: '/reference/player-entities/',
	Sound: '/reference/sound-entities/',
	Triggers: '/reference/trigger-entities/',
};

function load() {
	if (process.argv[2]) return readFileSync(resolve(process.argv[2]), 'utf8');

	const file = join(site, '.api', 'script-api.json');
	mkdirSync(dirname(file), { recursive: true });
	execFileSync(
		'dotnet',
		['run', '--project', join(site, '..', 'SpectraEngine.Executable'), '--', `--export-script-api=${file}`],
		{ stdio: 'inherit' });
	return readFileSync(file, 'utf8');
}

const api = JSON.parse(load());
if (api.format !== FORMAT) {
	throw new Error(`script-api: the JSON is of format ${api.format} and this script reads format ${FORMAT}.`);
}

const classes = new Map(api.classes.map(described => [described.name, described]));
const libraries = new Map(api.libraries.map(library => [library.name, library]));

// A file name Astro leaves as it is: small letters, digits and hyphens.
const slug = name => name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const classRoute = name => `${BASE}/${slug(name)}/`;
const entityRoute = name => `${BASE}/entities/${slug(name)}/`;

// Text of a summary, as Markdown that draws it as it is.
const text = words => words
	.replace(/\\/g, '\\\\')
	.replace(/</g, '&lt;')
	.replace(/([*_`[\]])/g, '\\$1')
	.replace(/\r?\n/g, ' ');

// A code span, which may hold anything but a line break. In a table a bar
// would end the cell, also inside a code span.
const code = (source, inTable = false) => {
	const flat = source.replace(/\s+/g, ' ');
	const fence = flat.includes('`') ? '``' : '`';
	const span = `${fence}${flat}${fence}`;
	return inTable ? span.replace(/\|/g, '\\|') : span;
};

const cell = words => text(words).replace(/\|/g, '\\|');

const link = name =>
	classes.has(name) ? `[${text(name)}](${classRoute(name)})` : code(name);

const parameters = list => list.map(one => (one.name === '...' ? `...: ${one.type}` : `${one.name}: ${one.type}`)).join(', ');

const returns = type => (type ? `: ${type}` : '');

const old = member => (member.deprecated ? `\n\nDeprecated: use ${code(member.deprecated)}.` : '');

// The classes a class extends, nearest first.
function basesOf(described) {
	const bases = [];
	for (let base = classes.get(described.extends); base; base = classes.get(base.extends)) bases.push(base);
	return bases;
}

function kindOf(described) {
	if (described.isValue) return 'values';
	if (described.name === 'Instance' || basesOf(described).some(base => base.name === 'Instance')) return 'nodes';
	return 'objects';
}

function front(title, description, extra = []) {
	return [
		'---',
		`title: ${JSON.stringify(title)}`,
		`description: ${JSON.stringify(description.slice(0, 200))}`,
		...extra,
		'editUrl: false',
		'---',
		'',
		'',
	].join('\n');
}

function propertiesTable(properties) {
	const rows = properties.map(property => {
		const access = property.canSet ? 'Read and set' : 'Read only';
		const words = cell(property.summary) + (property.deprecated ? ` Deprecated: use ${code(property.deprecated, true)}.` : '');
		return `| ${code(property.name, true)} | ${code(property.type, true)} | ${access} | ${words} |`;
	});
	return ['| Property | Type | | |', '|---|---|---|---|', ...rows].join('\n');
}

// One function or method: a heading, each shape it can be called in, and its words.
function callable(member, shape) {
	const shapes = member.signatures.map(signature => `- ${code(shape(signature))}`).join('\n');
	return `### ${text(member.name)}\n\n${shapes}\n\n${text(member.summary)}${old(member)}`;
}

function signalSection(signal, owner) {
	const handed = signal.parameters.length === 0
		? 'A function connected to it is handed nothing.'
		: `A function connected to it is handed ${code(parameters(signal.parameters))}.`;
	const words = signal.summary ? `${text(signal.summary)}\n\n` : '';
	return `### ${text(signal.name)}\n\n${code(`${owner}.${signal.name}`)}\n\n${words}${handed}`;
}

function operatorsTable(described) {
	const rows = described.operators.map(op => {
		// The one operator with nothing on its right is the minus in front.
		const written = op.right ? `${described.name} ${op.operator} ${op.right}` : `-${described.name}`;
		return `| ${code(written, true)} | ${code(op.returns, true)} |`;
	});
	return ['| Written | Gives |', '|---|---|', ...rows].join('\n');
}

// What a class has from the classes it extends, by name, with where each is written down.
function inherited(described) {
	return basesOf(described)
		.filter(base => base.properties.length + base.methods.length + base.signals.length > 0)
		.map(base => {
			const names = [...base.properties, ...base.methods, ...base.signals].map(member => code(member.name)).join(', ');
			return `From ${link(base.name)}: ${names}.`;
		});
}

function classPage(described) {
	const library = libraries.get(described.name);
	const generic = described.typeParameters ? `<${described.typeParameters}>` : '';
	const parts = [];

	parts.push(`<span class="api-kind">${described.isValue ? 'Value' : 'Class'}</span>`);
	parts.push(text(described.summary));
	if (generic) parts.push(`Written with what it hands over: ${code(described.name + generic)}.`);

	const bases = basesOf(described);
	if (bases.length > 0) parts.push(`Extends ${bases.map(base => link(base.name)).join(', which extends ')}.`);

	const extendedBy = api.classes.filter(other => other.extends === described.name).map(other => link(other.name));
	const entities = api.entityClasses.filter(other => other.extends === described.name)
		.map(other => `[${text(other.name)}](${entityRoute(other.name)})`);
	if (extendedBy.length > 0) parts.push(`Extended by ${extendedBy.join(', ')}.`);
	if (entities.length > 0) parts.push(`Entity classes on such a node: ${entities.join(', ')}.`);

	if (library) {
		parts.push(`## The ${text(library.name)} library`);
		parts.push(text(library.summary));
		if (library.constants.length > 0) {
			const rows = library.constants.map(constant =>
				`| ${code(`${library.name}.${constant.name}`, true)} | ${code(constant.type, true)} | ${cell(constant.summary)} |`);
			parts.push(['| Constant | Type | |', '|---|---|---|', ...rows].join('\n'));
		}
		for (const fn of library.functions) {
			parts.push(callable(fn, signature => `${library.name}.${fn.name}(${parameters(signature.parameters)})${returns(signature.returns)}`));
		}
	}

	if (described.properties.length > 0) {
		parts.push('## Properties');
		parts.push(propertiesTable(described.properties));
	}

	if (described.methods.length > 0) {
		parts.push('## Methods');
		for (const method of described.methods) {
			parts.push(callable(method, signature => `${described.name}:${method.name}(${parameters(signature.parameters)})${returns(signature.returns)}`));
		}
	}

	if (described.signals.length > 0) {
		parts.push('## Signals');
		for (const signal of described.signals) parts.push(signalSection(signal, described.name));
	}

	if (described.operators.length > 0) {
		parts.push('## Operators');
		parts.push(operatorsTable(described));
	}

	const fromBases = inherited(described);
	if (fromBases.length > 0) {
		parts.push('## Inherited');
		parts.push(fromBases.join('\n\n'));
	}

	if (described.childType) {
		parts.push('## Children');
		parts.push(`A name that is no member is the name of a child: ${code('workspace.Door')}. A member wins over a child of the same name.`);
	}

	return front(described.name, described.summary) + parts.join('\n\n') + '\n';
}

function libraryPage(library) {
	const parts = [`<span class="api-kind">Library</span>`, text(library.summary)];
	if (library.constants.length > 0) {
		const rows = library.constants.map(constant =>
			`| ${code(`${library.name}.${constant.name}`, true)} | ${code(constant.type, true)} | ${cell(constant.summary)} |`);
		parts.push('## Constants', ['| Constant | Type | |', '|---|---|---|', ...rows].join('\n'));
	}

	parts.push('## Functions');
	for (const fn of library.functions) {
		parts.push(callable(fn, signature => `${library.name}.${fn.name}(${parameters(signature.parameters)})${returns(signature.returns)}`));
	}

	return front(library.name, library.summary) + parts.join('\n\n') + '\n';
}

// "Door. An entity class of the group Movers." gives Movers.
const groupOf = described => /An entity class of the group (.+)\.$/.exec(described.summary)?.[1] ?? '';

function entityPage(described) {
	const group = groupOf(described);
	const page = ENTITY_PAGES[group];
	const parts = [`<span class="api-kind">Entity class</span>`, text(described.summary)];

	parts.push(page
		? `What its settings, inputs and outputs do is in the [entity reference](${page}#${described.name}).`
		: 'What its settings, inputs and outputs do is for its project to say.');

	const base = classes.get(described.extends);
	if (base) {
		parts.push(`A node of this class is of the class ${link(base.name)} too, and has every member of it.`);
	}

	parts.push(`A script says that a node is of this class with a cast: ${code(`local node = workspace.Name :: ${described.name}`)}. The checker then knows the inputs and outputs below.`);

	parts.push('## Inputs');
	if (described.methods.length === 0) {
		parts.push('It takes no input.');
	} else {
		parts.push('An input is a method. A call puts the input in the level\'s queue, and the entity gets it when its turn comes.');
		const rows = described.methods.flatMap(method => method.signatures.map(signature => {
			const takes = signature.parameters.length === 0 ? 'Nothing' : code(parameters(signature.parameters), true);
			return `| ${code(`node:${method.name}(${signature.parameters.map(one => one.name).join(', ')})`, true)} | ${takes} |`;
		}));
		parts.push(['| Call | Takes |', '|---|---|', ...rows].join('\n'));
	}

	parts.push('## Outputs');
	if (described.signals.length === 0) {
		parts.push('It fires no output.');
	} else {
		parts.push('An output is a signal, with `Connect`, `Once` and `Wait`.');
		const rows = described.signals.map(signal => {
			const hands = signal.parameters.length === 0 ? 'Nothing' : code(parameters(signal.parameters), true);
			return `| ${code(`node.${signal.name}`, true)} | ${hands} |`;
		});
		parts.push(['| Signal | Hands a function |', '|---|---|', ...rows].join('\n'));
	}

	return front(described.name, described.summary) + parts.join('\n\n') + '\n';
}

function indexPage() {
	const parts = [];
	parts.push('Everything a script can reach. These pages are written from the engine\'s own description of its script API, the one Visual Studio Code completes from, so they say what this build has.');
	parts.push('New to scripts here? Start with [Write your first script](/guides/write-your-first-script/). From Roblox? Read [Coming from Roblox](/start/coming-from-roblox/).');

	parts.push('## Globals');
	const globals = api.globals.map(global =>
		`| ${code(global.name, true)} | ${classes.has(global.type) ? link(global.type) : code(global.type, true)} | ${cell(global.summary)} |`);
	parts.push(['| Global | Type | |', '|---|---|---|', ...globals].join('\n'));

	parts.push('## Functions');
	for (const fn of api.functions) {
		parts.push(callable(fn, signature => `${fn.name}(${parameters(signature.parameters)})${returns(signature.returns)}`));
	}

	parts.push('Luau\'s own globals and libraries are there as well: `print`, `error`, `pcall`, `typeof`, `math`, `string`, `table` and the rest.');

	parts.push('## Libraries');
	const libraryRows = api.libraries.map(library => {
		const route = classes.has(library.name) ? classRoute(library.name) : `${BASE}/${slug(library.name)}/`;
		return `| [${text(library.name)}](${route}) | ${cell(library.summary)} |`;
	});
	parts.push(['| Library | |', '|---|---|', ...libraryRows].join('\n'));

	const groups = [
		['nodes', 'Nodes', 'What is in the level\'s tree.'],
		['values', 'Values', 'Held by value: one never changes, and every method gives a new one.'],
		['objects', 'Other objects', 'Services, signals, and what the player and its mover are handed.'],
	];
	parts.push('## Classes');
	for (const [kind, title, words] of groups) {
		const rows = api.classes.filter(described => kindOf(described) === kind).map(described =>
			`| ${link(described.name)} | ${described.extends ? link(described.extends) : ''} | ${cell(described.summary)} |`);
		parts.push(`### ${title}`, words, ['| Class | Extends | |', '|---|---|---|', ...rows].join('\n'));
	}

	parts.push('## Entity classes');
	parts.push('An entity class has its inputs as methods and its outputs as signals. A script says that a node is of one with a cast: `workspace.Door :: func_door`.');
	const entityRows = api.entityClasses.map(described =>
		`| [${text(described.name)}](${entityRoute(described.name)}) | ${described.extends ? link(described.extends) : ''} | ${cell(described.summary)} |`);
	parts.push(['| Class | On a | |', '|---|---|---|', ...entityRows].join('\n'));

	return front('Script API', 'Everything a script can reach: globals, libraries, classes and entity classes.') + parts.join('\n\n') + '\n';
}

// A route is the page's address on the site, and its file is named after it.
function write(route, page) {
	const target = join(out, ...route.slice(BASE.length).split('/').filter(Boolean)) + '.md';
	mkdirSync(dirname(target), { recursive: true });
	writeFileSync(target, page);
}

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'index.md'), indexPage());

for (const described of api.classes) write(classRoute(described.name), classPage(described));
for (const library of api.libraries.filter(one => !classes.has(one.name))) {
	write(`${BASE}/${slug(library.name)}/`, libraryPage(library));
}
for (const described of api.entityClasses) write(entityRoute(described.name), entityPage(described));

// The sidebar's groups, in the order the overview lists them.
const item = (label, route) => ({ label, link: route });
const of = kind => api.classes.filter(described => kindOf(described) === kind)
	.map(described => item(described.name, classRoute(described.name)));
const sidebar = [
	item('Overview', `${BASE}/`),
	{ label: 'Nodes', collapsed: true, items: of('nodes') },
	{ label: 'Values', collapsed: true, items: of('values') },
	{ label: 'Other objects', collapsed: true, items: of('objects') },
	{
		label: 'Libraries',
		collapsed: true,
		items: api.libraries.filter(one => !classes.has(one.name)).map(one => item(one.name, `${BASE}/${slug(one.name)}/`)),
	},
	{
		label: 'Entity classes',
		collapsed: true,
		items: api.entityClasses.map(described => item(described.name, entityRoute(described.name))),
	},
];
writeFileSync(join(site, 'src', 'script-api-sidebar.json'), JSON.stringify(sidebar, null, '\t') + '\n');

const count = 1 + api.classes.length + api.libraries.filter(one => !classes.has(one.name)).length + api.entityClasses.length;
console.log(`script-api: wrote ${count} pages to ${out}`);
