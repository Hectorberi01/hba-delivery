import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../features/deliveries/delivery_providers.dart';
import '../features/deliveries/models.dart';
import '../features/rides/course_models.dart';
import '../features/rides/course_providers.dart';
import 'paiement_en_attente.dart';

final paiementsEnAttenteProvider = Provider<PaiementsEnAttente>(
  (ref) => PaiementsEnAttente(),
);

/// Les commandes que l'appareil se rappelle avoir envoyées sans voir le paiement
/// aboutir, la plus récente d'abord.
///
/// RAFRAICHI A LA MAIN, PAS EN CONTINU. Il est lu au demarrage et apres chaque
/// reprise ; une ecoute permanente ferait relire le stockage chiffre a chaque
/// reconstruction d'ecran pour une liste qui ne bouge presque jamais.
final commandesAPayerProvider = FutureProvider<List<CommandeAPayer>>(
  (ref) => ref.watch(paiementsEnAttenteProvider).lire(),
);

/// Ce que rend une reprise : la demande est repartie, ou la raison pour laquelle
/// il n'y a plus rien à reprendre.
sealed class IssueDeReprise {
  const IssueDeReprise();
}

/// Le service a rouvert l'intention : voici la page où envoyer le client.
///
/// L'ADRESSE EST REVENUE LE 3 OCTOBRE 2026, le jour même où elle avait disparu.
/// L'écran redevient donc ce qu'il était : il ouvre cette page.
final class ReprisePossible extends IssueDeReprise {
  const ReprisePossible(this.url);

  final String url;
}

/// La commande n'attend plus de paiement — payée entre-temps, ou abandonnée.
///
/// LES DEUX SE TRAITENT PAREIL ICI, ET C'EST VOLONTAIRE : dans les deux cas il
/// n'y a plus rien a payer, et l'ecran doit cesser de le proposer. Ce qui les
/// distingue se lit sur la commande elle-meme, pas sur ce retour.
final class RepriseSansObjet extends IssueDeReprise {
  const RepriseSansObjet(this.message);

  final String message;
}

/// Rejoue la commande gardée sur l'appareil pour relancer son paiement.
///
/// LE REJEU NE CREE RIEN ET NE CONSOMME PAS UN SECOND DEVIS. Le service reconnait
/// sa propre cle d'idempotence et rend la MEME commande avec la MEME intention de
/// paiement (point 30, tranche le 29 septembre 2026). C'est aussi pour cela que
/// la reprise passe par la route de creation et non par une route dediee : il
/// n'y en a pas.
///
/// ON OUBLIE DES QUE CE N'EST PLUS A PAYER. Une adresse vide veut dire que le
/// service n'a plus d'intention ouverte a offrir : la commande est payee, ou le
/// balayage l'a fermee. Dans les deux cas, la garder proposerait indefiniment
/// une reprise impossible.
///
/// UN WidgetRef ET NON UN Ref, ET LES DEUX NE SONT PAS INTERCHANGEABLES. Riverpod
/// en distingue deux : celui d'un provider, qui vit et meurt avec lui, et celui
/// d'un widget, lie a l'element de l'arbre. Cette fonction est appelee depuis un
/// bouton, donc c'est le second — et elle n'utilise de toute facon que « read » et
/// « invalidate », que les deux offrent.
Future<IssueDeReprise> reprendreLePaiement(
  WidgetRef ref,
  CommandeAPayer commande,
) async {
  final memoire = ref.read(paiementsEnAttenteProvider);

  if (commande.estUneCourse) {
    final creee = await ref
        .read(courseRepositoryProvider)
        .envoyer(idempotencyKey: commande.cle, corps: commande.corps);

    ref.invalidate(coursesActivesProvider);

    if (!creee.paiementOuvrable) {
      await memoire.oublier(commande.id);
      return const RepriseSansObjet(
        "Cette course n'attend plus de paiement. Consultez vos courses.",
      );
    }

    return ReprisePossible(creee.adresseDePaiement!);
  }

  final (_, adresse) = await ref
      .read(deliveryRepositoryProvider)
      .envoyer(idempotencyKey: commande.cle, corps: commande.corps);

  ref.invalidate(deliveriesProvider);

  if (adresse == null || adresse.isEmpty) {
    await memoire.oublier(commande.id);
    return const RepriseSansObjet(
      "Cette livraison n'attend plus de paiement. Consultez vos courses.",
    );
  }

  return ReprisePossible(adresse);
}

/// Oublie l'entrée locale quand la commande n'attend plus d'être payée.
///
/// POURQUOI ON DEMANDE AU SERVEUR PLUTOT QUE DE CROIRE L'APPLICATION. Elle ne
/// sait rien du paiement : il se joue sur la page du fournisseur, et c'est
/// l'opérateur qui répond — à FedaPay, qui nous appelle ensuite. Seul le webhook
/// signé fait foi (ADR 0017), et il arrive chez nous, pas chez lui. La seule question à laquelle on puisse répondre ici est donc :
/// « le service attend-il encore d'être payé ? »
///
/// LE TROU QU'IL COMBLE. L'entrée n'était effacée que par une reprise rendue
/// sans objet, ou par la purge au quart d'heure. Entre les deux, un client qui
/// venait de payer voyait l'accueil lui proposer de reprendre le paiement d'une
/// commande déjà réglée — et un rejeu l'aurait renvoyé sur un refus.
///
/// SILENCIEUX EN CAS D'ECHEC, ET C'EST VOULU : ce n'est qu'un ménage local.
/// Rien n'est perdu si l'appel ne passe pas — la purge au quart d'heure reste,
/// et la reprise sait déjà reconnaître une commande sans objet.
Future<void> oublierSiReglee(
  WidgetRef ref, {
  required bool estUneCourse,
  required String id,
}) async {
  try {
    final attendEncore = estUneCourse
        ? (await ref.read(courseRepositoryProvider).lire(id)).etat ==
            EtatDeCourse.demandee
        : (await ref.read(deliveryRepositoryProvider).byId(id)).status ==
            DeliveryStatus.pendingPayment;

    if (attendEncore) return;

    await ref.read(paiementsEnAttenteProvider).oublier(id);
    ref.invalidate(commandesAPayerProvider);
  } on Object {
    // Hors ligne, service injoignable, commande introuvable : on laisse
    // l'entrée vivre sa quinzaine de minutes.
  }
}
