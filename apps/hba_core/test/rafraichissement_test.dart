import 'package:flutter_test/flutter_test.dart';
import 'package:hba_core/hba_core.dart';

/// Ce que l'application conclut d'un rafraichissement refuse.
///
/// POURQUOI CES TESTS EXISTENT. Jusqu'au 30 septembre 2026, tout echec de
/// rafraichissement valait « session perdue ». Cote livreur, la session perdue
/// PURGE la file d'actions hors ligne : une coupure reseau au mauvais moment
/// effacait une remise deja faite, laissait la course ouverte, et renvoyait le
/// livreur a l'ecran de connexion. Le cas se produit exactement la ou le reseau
/// est mauvais, c'est-a-dire tout le temps.
///
/// Ce qui est verifie ici n'est donc pas « la bonne reponse » mais UNE FRONTIERE :
/// ce qui a le droit de fermer une session, et tout le reste.
void main() {
  IssueDeRafraichissement issue({int? statut, String? code}) =>
      issueDUnRefusDeRafraichissement(statutHttp: statut, codeMetier: code);

  group('Ce qui ferme la session', () {
    test('403 : le refus canonique du serveur', () {
      // Les quatre chemins de refus d'Identity — jeton inconnu, revoque,
      // expire, rejoue — levent une ForbiddenException, traduite en 403.
      expect(issue(statut: 403), IssueDeRafraichissement.sessionPerdue);
    });

    test("404 : le compte a ete efface, la session n'a plus de titulaire", () {
      expect(issue(statut: 404), IssueDeRafraichissement.sessionPerdue);
    });

    test('401 sur la route de rafraichissement elle-meme', () {
      expect(issue(statut: 401), IssueDeRafraichissement.sessionPerdue);
    });

    test('409 accompagne de REFRESH_TOKEN_REUSED', () {
      expect(
        issue(statut: 409, code: 'REFRESH_TOKEN_REUSED'),
        IssueDeRafraichissement.sessionPerdue,
      );
    });
  });

  group('Ce qui ne la ferme pas', () {
    test('aucune reponse : le transport a echoue', () {
      // Le cas du livreur qui remonte d'un sous-sol. C'est celui qui coutait
      // une remise.
      expect(issue(statut: null), IssueDeRafraichissement.indisponible);
    });

    test('400 : requete mal formee, donc un defaut de NOTRE cote', () {
      // Le rapport d'audit recommandait de deconnecter sur 400. Il avait tort :
      // ce statut vient d'un VALIDATION_FAILED, et faire payer a l'utilisateur
      // un bug de l'application est precisement ce qu'on corrige.
      expect(issue(statut: 400), IssueDeRafraichissement.indisponible);
    });

    test('les pannes de service : 500, 502, 503, 504', () {
      // Le redeploiement de la passerelle ejectait tous les clients.
      for (final statut in [500, 502, 503, 504]) {
        expect(
          issue(statut: statut),
          IssueDeRafraichissement.indisponible,
          reason: 'HTTP $statut ne dit rien de la session',
        );
      }
    });

    test("429 : on nous demande d'attendre, pas de partir", () {
      expect(issue(statut: 429), IssueDeRafraichissement.indisponible);
    });

    test('408 : expiration cote serveur', () {
      expect(issue(statut: 408), IssueDeRafraichissement.indisponible);
    });

    test('409 sans code, ou avec un autre code', () {
      // 409 est la traduction par defaut de TOUTE regle metier refusee
      // (FailedPrecondition) : le statut seul ne prouve rien.
      expect(issue(statut: 409), IssueDeRafraichissement.indisponible);
      expect(
        issue(statut: 409, code: 'AUTRE_CHOSE'),
        IssueDeRafraichissement.indisponible,
      );
    });

    test('un statut inattendu ne ferme jamais la session', () {
      // La regle de repli, et celle qui compte le plus : dans le doute, on ne
      // detruit rien.
      for (final statut in [200, 201, 204, 301, 418, 451, 599]) {
        expect(
          issue(statut: statut),
          IssueDeRafraichissement.indisponible,
          reason: 'HTTP $statut',
        );
      }
    });
  });
}
