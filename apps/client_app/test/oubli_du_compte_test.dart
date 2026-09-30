import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hba_client/core/providers.dart';
import 'package:hba_client/features/auth/auth_repository.dart';
import 'package:hba_client/features/auth/session_controller.dart';
import 'package:hba_client/features/deliveries/delivery_providers.dart';
import 'package:hba_core/hba_core.dart';

/// Ce qu'un compte laisse derriere lui quand il s'en va.
///
/// POURQUOI CES TESTS EXISTENT. C'est le constat S6 de l'audit du 30 septembre
/// 2026 : aucun fournisseur de donnees n'etait autoDispose, et ni la deconnexion
/// ni la perte de session n'en invalidait un seul. Deux personnes partagent un
/// telephone, ou un vendeur en fait la demonstration : le compte A se
/// deconnecte, le compte B se connecte, et B voit le profil de A, ses adresses,
/// ses courses, et LES NUMEROS DE TELEPHONE DES DESTINATAIRES DE A.
///
/// CE QUI EST EPROUVE ICI N'EST PAS UN ECRAN, C'EST LE MECANISME. La regle tient
/// a une dependance : tout ce qui porte des donnees du compte passe par un
/// depot, tout depot passe par « apiClientProvider », et ce client surveille
/// « generationDuCompteProvider ». Le jour ou quelqu'un retire ce « watch », ces
/// tests tombent — et c'est exactement leur raison d'etre, parce que le defaut,
/// lui, ne se voit pas : l'ecran de B n'affiche pas une erreur, il affiche la
/// vie de quelqu'un d'autre.
///
/// CE N'EST PAS UNE HYPOTHESE : LE DEUXIEME TEST A DEJA SERVI. La premiere
/// version de la correction faisait surveiller un BOOLEEN (« y a-t-il
/// quelqu'un de connecte »). Riverpod ne reconstruit un dependant que si la
/// valeur recalculee differe de la precedente : A part, B arrive, le booleen
/// repasse de vrai a vrai sans que personne l'ait lu entre les deux, et B
/// recevait le depot de A. Le test a echoue, le mecanisme a ete refait en
/// compteur. C'est le seul de ces quatre tests qui parcourt la sequence
/// ENTIERE — sortie PUIS entree — et c'est pour cela qu'il l'a vu.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  ProviderContainer conteneur() {
    final c = ProviderContainer();
    addTearDown(c.dispose);
    return c;
  }

  test('la deconnexion reconstruit le client d\'API', () async {
    final c = conteneur();

    c.read(sessionProvider.notifier).signedIn(
          const SignedInCustomer(displayName: 'Compte A'),
        );

    final pendant = c.read(apiClientProvider);

    c.read(sessionProvider.notifier).signedOut();

    expect(
      identical(pendant, c.read(apiClientProvider)),
      isFalse,
      reason: 'sans reconstruction, tout ce qui en depend garde ses donnees',
    );
  });

  test('et avec lui TOUS les depots qui en dependent', () async {
    final c = conteneur();

    c.read(sessionProvider.notifier).signedIn(
          const SignedInCustomer(displayName: 'Compte A'),
        );

    final depotDeA = c.read(deliveryRepositoryProvider);

    c.read(sessionProvider.notifier).signedOut();
    c.read(sessionProvider.notifier).signedIn(
          const SignedInCustomer(displayName: 'Compte B'),
        );

    // LE DEPOT EST CE QUI TIENT LE CACHE : « deliveriesProvider » le surveille,
    // donc un depot neuf veut dire une liste redemandee. C'est la chaine
    // entiere qu'on eprouve ici, pas une invalidation ecrite a la main.
    expect(identical(depotDeA, c.read(deliveryRepositoryProvider)), isFalse);
  });

  test('le renommage, lui, ne jette rien', () async {
    // LE SECOND BORD DE LA REGLE, et il compte autant. Si le moindre changement
    // de session vidait les caches, corriger son prenom ferait repartir toutes
    // les requetes de l'application — sur une 3G a Cotonou, le client le
    // sentirait passer. Seul un CHANGEMENT DE COMPTE doit tout effacer.
    final c = conteneur();

    c.read(sessionProvider.notifier).signedIn(
          const SignedInCustomer(displayName: 'Compte A'),
        );

    final avant = c.read(apiClientProvider);

    c.read(sessionProvider.notifier).renommer('Compte A, corrige');

    expect(identical(avant, c.read(apiClientProvider)), isTrue);
  });

  test('la course en attente ne suit pas le compte suivant', () async {
    // CELLE-LA NE VIENT D'AUCUNE REQUETE, donc le mecanisme ci-dessus ne la
    // couvre pas. Laissee en place, elle envoie l'accueil du compte SUIVANT
    // chercher une course qui n'est pas la sienne : la fiche repond 403 ou 404,
    // rien ne le rattrape, et l'accueil de B tourne indefiniment.
    final c = conteneur();

    c.read(sessionProvider.notifier).signedIn(
          const SignedInCustomer(displayName: 'Compte A'),
        );
    c.read(courseEnAttenteProvider.notifier).state = 'course-de-A';

    c.read(sessionProvider.notifier).signedOut();

    expect(c.read(courseEnAttenteProvider), isNull);
  });
}
