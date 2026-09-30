import 'package:hba_ui/hba_ui.dart';

/// La politique de confidentialite de l'application cliente.
///
/// ECRITE DEPUIS LE CODE DE CETTE APPLICATION-CI. Celle du livreur comporte une
/// section sur les rapports de plantage envoyes a Firebase Crashlytics :
/// l'application cliente n'en envoie aucun — il n'y a ni dependance, ni appel.
/// Recopier la section aurait fait declarer une transmission a Google qui
/// n'existe pas, ce qui est aussi faux que d'en taire une qui existerait.
///
/// LA POSITION DU CLIENT N'EST PAS CELLE DU LIVREUR. Elle ne part pas toutes
/// les vingt secondes et ne sert a aucun suivi : elle sert a demander « quels
/// livreurs sont autour de ce point », une question a la fois, et rien ne
/// l'ecrit.
///
/// LES DUREES DE CONSERVATION N'Y FIGURENT PAS PARCE QU'ELLES N'EXISTENT PAS,
/// exactement comme cote livreur. Ecrire un delai serait inventer une regle.
const confidentialiteClient = DocumentLegal(
  titre: 'Politique de confidentialité',
  chapeau:
      "Ce que l'application collecte, pourquoi, qui peut le consulter, et ce "
      'que vous pouvez en demander.',
  version: 'v0.2',
  miseAJour: '28 septembre 2026',
  sections: [
    SectionLegale(
      titre: 'Qui traite vos données',
      paragraphes: [
        'HBA, éditeur de cette application et de la plateforme de livraison à '
            'laquelle elle vous donne accès.',
      ],
      aTrancher:
          "La raison sociale exacte, l'adresse du siège et le point de contact "
          'chargé des données personnelles doivent figurer ici. Ils ne sont pas '
          "encore renseignés dans l'application.",
    ),
    SectionLegale(
      titre: 'Ce que nous collectons',
      points: [
        'Votre numéro de téléphone : il identifie votre compte et reçoit votre '
            'code de connexion.',
        'Votre nom, et votre adresse e-mail si vous en donnez une.',
        'Vos adresses favorites : le point que vous posez, le nom que vous lui '
            'donnez et le repère que vous écrivez.',
        'Vos courses : les deux points, les repères, le nom et le téléphone du '
            'destinataire, la description du colis.',
        'Le montant de vos courses et la référence de la transaction chez '
            'notre prestataire de paiement.',
      ],
    ),
    SectionLegale(
      titre: 'Votre position',
      paragraphes: [
        "L'application demande votre position pour CENTRER LA CARTE et vous "
            'montrer les livreurs autour de vous. Vous pouvez refuser : la '
            'carte s\'ouvre alors sur Cotonou, et tout le reste fonctionne.',
        'Ce point part au serveur avec une seule question : quels livreurs '
            'sont à proximité. La réponse ne contient que des COORDONNEES — ni '
            'nom, ni identifiant, ni plaque — tant qu\'aucun livreur ne vous '
            'est attribué.',
        'Elle n\'est envoyée que pendant que vous regardez la carte, et '
            'seulement quand elle a bougé d\'au moins cinq cents mètres. '
            'L\'application ne construit aucun historique de vos déplacements, '
            'et n\'envoie rien lorsqu\'elle est fermée.',
      ],
    ),
    SectionLegale(
      titre: 'Vos moyens de paiement',
      paragraphes: [
        'Le paiement se fait sur la page de notre prestataire FedaPay, '
            'affichée dans l\'application. Votre numéro de carte et votre code '
            'mobile money sont saisis chez lui : ils NE PASSENT PAS par HBA, '
            'qui ne les reçoit ni ne les conserve.',
        'HBA garde le montant, la date, et la référence que le prestataire '
            'lui renvoie — de quoi rattacher un paiement à une course, pas de '
            'quoi en déclencher un autre.',
      ],
    ),
    SectionLegale(
      titre: 'Le destinataire de vos colis',
      paragraphes: [
        'Quand vous commandez, vous nous donnez le nom et le téléphone d\'une '
            'autre personne. Nous les transmettons au livreur pour qu\'il '
            'remette le colis, et nous ne les utilisons pour rien d\'autre : '
            'ni message commercial, ni création de compte à son nom.',
      ],
    ),
    SectionLegale(
      titre: 'Ce que les autres voient de vous',
      points: [
        'Le livreur à qui votre course est attribuée voit les deux points, '
            'les repères, et le contact sur place — celui du départ et celui '
            'de l\'arrivée. Avant l\'attribution, aucun livreur ne voit quoi '
            'que ce soit de vous.',
        'Les livreurs que vous voyez sur la carte ne vous voient pas.',
        'LE CODE DE REMISE N\'EST MONTRE NI AU LIVREUR NI A HBA. Seuls vous et '
            'le destinataire le connaissez : c\'est ce qui lui donne sa valeur '
            'de preuve.',
        'Le personnel de HBA voit vos courses. Vos adresses enregistrées et '
            'votre adresse e-mail ne sont visibles que de l\'administration et '
            'du support, pas des équipes qui supervisent les courses.',
      ],
    ),
    SectionLegale(
      titre: 'Vos messages',
      paragraphes: [
        'Votre code de connexion part par SMS. Il peut partir par WhatsApp à '
            'la place, si vous l\'avez demandé depuis votre profil — et '
            'seulement dans ce cas. Ce choix se retire au même endroit, par le '
            'même geste.',
      ],
    ),
    SectionLegale(
      titre: 'Combien de temps',
      paragraphes: [
        'Vos données de compte sont conservées tant que votre compte existe.',
      ],
      aTrancher:
          'Les durées de conservation ne sont pas arrêtées : ni celle des '
          'courses, ni celle des preuves de remise. Le mécanisme de purge '
          "existe dans le système, mais aucun délai n'y est encore inscrit.",
    ),
    SectionLegale(
      titre: 'Vos droits',
      paragraphes: [
        'Vous pouvez consulter et corriger votre nom, votre adresse e-mail et '
            'vos adresses enregistrées à tout moment depuis votre profil.',
        'Vous pouvez supprimer votre compte depuis votre profil, en bas de '
            "l'écran. Vous êtes déconnecté aussitôt, et la suppression a lieu "
            '30 jours plus tard : vous reconnecter avant cette date la '
            'annule, et vous retrouvez tout.',
        'Au terme de ce délai, votre profil, vos adresses enregistrées et '
            'votre photo sont effacés pour de bon. Les courses déjà '
            "effectuées restent dans l'historique de HBA — elles concernent "
            'aussi des livreurs et des destinataires — mais elles ne portent '
            'pas votre nom, et plus rien ne permet de les rattacher à vous.',
      ],
      aTrancher:
          'Deux choses échappent encore à cette suppression, et il faut le '
          'dire : les enregistrements de paiement et la trace des messages '
          "qui vous ont été envoyés. Ils portent votre numéro. La durée que "
          'la comptabilité impose de les conserver doit être vérifiée avant '
          "d'y toucher : effacer un justificatif que la loi oblige à garder "
          'serait une faute, et le garder sans le dire en serait une autre.',
    ),
    SectionLegale(
      titre: 'Nous écrire à ce sujet',
      paragraphes: [
        'Les coordonnées du support figurent dans votre profil et dans '
            "l'aide. Une demande concernant vos données personnelles passe par "
            'le même chemin.',
      ],
    ),
  ],
);
