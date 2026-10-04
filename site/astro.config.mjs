// @ts-check
import { existsSync, readFileSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Written by `npm run api`. Missing until that has run once.
const apiSidebar = new URL('./src/api-sidebar.json', import.meta.url);
const apiGroups = existsSync(apiSidebar) ? JSON.parse(readFileSync(apiSidebar, 'utf8')) : [];

export default defineConfig({
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
					label: 'Contributing',
					items: [{ label: 'Writing docs', slug: 'contributing/writing-docs' }],
				},
				{
					label: 'Reference',
					items: [{ label: 'Engine API (C#)', collapsed: true, items: apiGroups }],
				},
			],
		}),
	],
});
