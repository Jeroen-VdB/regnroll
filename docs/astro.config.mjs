// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Published at https://regnroll.github.io (org root pages repo).
// Forks serving under a project path can set DOCS_BASE=/repo-name/ at build time.
export default defineConfig({
  site: 'https://regnroll.github.io',
  base: process.env.DOCS_BASE ?? '/',
  integrations: [
    starlight({
      title: 'Regnroll',
      description: 'Entra ID app registration secret & certificate automation',
      social: [
        {
          icon: 'github',
          label: 'GitHub',
          href: 'https://github.com/Jeroen-VdB/regnroll',
        },
      ],
      sidebar: [
        {
          label: 'Start here',
          items: ['getting-started', 'permissions'],
        },
        {
          label: 'Guides',
          items: ['admin-guide', 'customer-guide', 'email-templates'],
        },
        {
          label: 'Reference',
          items: ['architecture', 'configuration', 'security-model'],
        },
      ],
    }),
  ],
});
