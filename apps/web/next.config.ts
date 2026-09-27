import type { NextConfig } from 'next';

const config: NextConfig = {
  reactStrictMode: true,

  // AUCUNE REECRITURE VERS LA PASSERELLE ICI, ET C'EST VOLONTAIRE. Un rewrite
  // enverrait le navigateur parler directement a la gateway, donc il faudrait
  // que le navigateur detienne le jeton. Tout passe par /api/hba/*, un
  // gestionnaire de route qui ajoute le jeton cote serveur : le navigateur ne
  // voit jamais ni jeton d'acces ni jeton de rafraichissement.
};

export default config;
