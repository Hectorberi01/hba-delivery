import Link from 'next/link';
import { Marque } from '@/composants/entete';

export const metadata = { title: 'Mot de passe oublié — Console HBA Delivery' };

export default function PageMotDePasseOublie() {
  return (
    <main className="grid min-h-screen place-items-center px-6 py-12">
      <div className="relief w-full max-w-[460px] rounded-[var(--radius-carte)] p-8">
        <div className="flex justify-center">
          <Marque grand />
        </div>

        <h1 className="mt-7 text-center text-xl font-semibold text-encre">Mot de passe oublié</h1>

        {/* PAS DE FORMULAIRE QUI NE MENE NULLE PART.
            Identity n'expose aujourd'hui que POST /api/web/v1/auth/password,
            qui exige d'etre deja connecte. Il n'y a ni demande de lien, ni
            jeton de reinitialisation. Un champ e-mail et un bouton
            « Envoyer le lien » afficheraient « verifiez votre boite » pour un
            courriel que personne n'envoie : l'utilisateur attendrait, puis
            appellerait le support de toute facon. */}
        <p className="mt-4 text-sm leading-relaxed text-encre-2">
          La réinitialisation en libre-service n&apos;existe pas encore. Un administrateur peut
          poser un nouveau mot de passe sur votre compte ; il vous sera demandé d&apos;en choisir un
          autre à la première connexion.
        </p>

        <div className="creux-doux mt-5 rounded-xl px-4 py-4 text-sm">
          <p className="font-medium text-encre-2">Ce qui manque côté service</p>
          <ul className="mt-2 space-y-1 text-encre-3">
            <li>
              <code className="creux-doux rounded px-1.5 py-0.5 text-[13px]">
                POST /api/web/v1/auth/password/forgot
              </code>{' '}
              — demande d&apos;un lien, réponse identique que le compte existe ou non.
            </li>
            <li>
              <code className="creux-doux rounded px-1.5 py-0.5 text-[13px]">
                POST /api/web/v1/auth/password/reset
              </code>{' '}
              — consommation du jeton, à usage unique et de courte durée.
            </li>
          </ul>
        </div>

        <p className="mt-6 text-center text-sm">
          <Link href="/connexion" className="text-marque hover:underline">
            Retour à la connexion
          </Link>
        </p>
      </div>
    </main>
  );
}
