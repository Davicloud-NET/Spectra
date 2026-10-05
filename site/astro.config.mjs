// @ts-check
import { existsSync, readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Written by `npm run api`. Missing until that has run once.
const apiSidebar = new URL('./src/api-sidebar.json', import.meta.url);
const apiGroups = existsSync(apiSidebar) ? JSON.parse(readFileSync(apiSidebar, 'utf8')) : [];

export default defineConfig({
	// DocFX rewrites .api while the dev server runs, and the watcher trips on it.
	vite: { server: { watch: { ignored: ['**/.api/**'] } } },
	integrations: [
		starlight({
			title: 'Spectra Engine',
			logo: { src: './src/assets/logo.png' },
			favicon: '/favicon.ico',
			customCss: ['./src/styles/spectra.css'],
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/Davicloud-NET/Spectra' }],
			editLink: { baseUrl: 'https://github.com/Davicloud-NET/Spectra/edit/master/site/' },
			sidebar: [
				{
					label: 'Start here',
					items: [{ label: 'Build from source', slug: 'start/build-from-source' }],
				},
				{
					label: 'Concepts',
					items: [
						{ label: 'The scene', slug: 'concepts/scene' },
						{ label: 'Blocks, parts and cuts', slug: 'concepts/blocks-parts-and-cuts' },
						{ label: 'Units and axes', slug: 'concepts/units-and-axes' },
						{ label: 'Projects and levels', slug: 'concepts/projects-and-levels' },
						{ label: 'Entities and wiring', slug: 'concepts/entities-and-wiring' },
					],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'Make a door that opens', slug: 'guides/make-a-door-that-opens' },
						{ label: 'Cook and run a project', slug: 'guides/cook-and-run-a-project' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'Keyboard and mouse', slug: 'reference/keyboard' },
						{ label: 'Logic entities', slug: 'reference/logic-entities' },
						{ label: 'Mover entities', slug: 'reference/mover-entities' },
						{ label: 'Trigger entities', slug: 'reference/trigger-entities' },
						{ label: 'Player entities', slug: 'reference/player-entities' },
						{ label: 'Console', slug: 'reference/console' },
						{ label: 'Material files', slug: 'reference/material-files' },
						{ label: 'Level files', slug: 'reference/level-files' },
						{ label: 'scook', slug: 'reference/scook' },
						{ label: 'Engine API (C#)', collapsed: true, items: apiGroups },
					],
				},
				{
					label: 'Contributing',
					items: [{ label: 'Writing docs', slug: 'contributing/writing-docs' }],
				},
			],
		}),
	],
});
