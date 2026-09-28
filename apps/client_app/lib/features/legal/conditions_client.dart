import 'package:hba_ui/hba_ui.dart';

/// Les conditions du client.
///
/// ECRITES DEPUIS L'APPLICATION CLIENTE, PAS ADAPTEES DE CELLES DU LIVREUR.
/// Les deux textes partagent leur forme, pas leur contenu : le livreur accepte
/// des offres et se fait verser, le client commande et paie. Recopier l'un en
/// changeant les mots aurait produit des regles qui ne correspondent a aucun
/// controle dans le code.
///
/// CHAQUE REGLE CI-DESSOUS EXISTE DANS LE SYSTEME. Le prix est fige a la
/// confirmation parce que le devis est CONSOMME ; le code de remise n'est
/// jamais montre au livreur parce que rien ne le lui envoie ; le paiement se
/// valide sur le telephone du payeur parce qu'aucun prelevement serveur
/// n'existe chez FedaPay. Ce qui n'est pas tranche est ecrit comme tel.
const conditionsClient = DocumentLegal(
  titre: "Conditions d'utilisation",
  chapeau:
      'Ce que vous pouvez attendre de HBA quand vous faites livrer un colis, '
      "et ce que HBA attend de vous. Ces conditions décrivent l'application "
      "telle qu'elle est aujourd'hui.",
  version: 'v0.1',
  miseAJour: '28 septembre 2026',
  sections: [
    SectionLegale(
      titre: 'Le rôle de HBA',
      paragraphes: [
        'HBA met en relation des clients et des livreurs, calcule le prix '
            "d'une course, la confie à un livreur et en suit le déroulement.",
        "HBA ne transporte pas lui-même : c'est un livreur indépendant qui "
            'prend votre colis et le remet.',
      ],
    ),
    SectionLegale(
      titre: 'Votre compte',
      paragraphes: [
        'Votre compte est votre NUMERO DE TELEPHONE. Vous vous connectez avec '
            'un code reçu par message ; il n\'y a pas de mot de passe à '
            'retenir, ni à perdre.',
        'Changer de numéro, c\'est changer de compte : le numéro identifie le '
            'compte, il ne se modifie pas depuis un formulaire.',
      ],
    ),
    SectionLegale(
      titre: 'Le prix',
      paragraphes: [
        'Le prix vous est annoncé AVANT que vous confirmiez, à partir des deux '
            'points que vous avez posés. Il est FIGE au moment de la '
            'confirmation : il ne change plus, même si la grille tarifaire '
            'évolue ensuite, et même si le trajet réel s\'avère plus long.',
        'Le prix annoncé est celui que vous payez. La part de HBA y est déjà '
            'comprise : rien ne s\'ajoute à l\'arrivée.',
      ],
    ),
    SectionLegale(
      titre: 'Le paiement',
      points: [
        'Le paiement se fait par mobile money ou par carte, sur la page '
            'sécurisée de notre prestataire FedaPay, affichée dans '
            'l\'application.',
        'AUCUN DEBIT N\'EST POSSIBLE SANS VOUS : vous validez sur votre '
            'téléphone, avec votre code. Ni HBA ni le livreur ne peuvent '
            'déclencher un paiement à votre place.',
        'Vos identifiants de paiement — numéro de carte, code mobile money — '
            'ne passent jamais par HBA : ils sont saisis chez le prestataire.',
        'La demande de paiement part sur LE NUMERO DE VOTRE COMPTE.',
      ],
      aTrancher:
          'Le remboursement — délai, canal, et qui décide — n\'est pas arrêté. '
          'Aucune règle de remboursement n\'est aujourd\'hui appliquée par le '
          'système.',
    ),
    SectionLegale(
      titre: 'Le destinataire',
      paragraphes: [
        'Vous indiquez le nom et le téléphone de la personne qui reçoit le '
            'colis. En les donnant, vous déclarez être en droit de le faire : '
            'HBA les utilise pour livrer, et pour rien d\'autre.',
        'Le destinataire n\'est pas forcément vous, et ce n\'est pas lui qui '
            'paie : le compte qui commande est celui qui règle.',
      ],
    ),
    SectionLegale(
      titre: 'La remise du colis',
      points: [
        'Un code à six chiffres accompagne chaque course. Il est connu de vous '
            'et du destinataire.',
        'LE LIVREUR NE LE REÇOIT JAMAIS. Le destinataire le lui dicte au '
            'moment de la remise : c\'est ce qui prouve que le colis a été '
            'remis à la bonne personne, et non qu\'un bouton a été pressé.',
        'Ne communiquez ce code à personne avant la remise, pas même au '
            'livreur qui le demanderait à l\'avance.',
      ],
    ),
    SectionLegale(
      titre: 'Ce que vous ne pouvez pas faire transporter',
      paragraphes: [
        'Vous restez responsable du contenu de votre colis. Tout ce dont la '
            'possession ou le transport est interdit par la loi béninoise en '
            'est exclu, ainsi que les espèces, les animaux vivants et tout ce '
            'qui est dangereux pour le livreur.',
      ],
      aTrancher:
          'La liste précise des objets interdits, et ce qui se passe quand un '
          'colis en contient — refus du livreur, annulation, signalement — '
          "n'est pas arrêtée. Le système n'applique aujourd'hui aucun contrôle "
          'de contenu.',
    ),
    SectionLegale(
      titre: 'Annuler une course',
      paragraphes: [
        "Vous pouvez annuler une course depuis l'application tant qu'elle n'a "
            'pas été livrée.',
      ],
      aTrancher:
          "Les conséquences d'une annulation ne sont pas arrêtées : aucun "
          "frais n'est appliqué, aucun remboursement n'est déclenché, et le "
          'moment à partir duquel une annulation devient tardive '
          "n'est pas défini. Le système enregistre l'annulation, il n'en tire "
          'aucune conséquence financière.',
    ),
    SectionLegale(
      titre: 'En cas de problème',
      paragraphes: [
        'Colis non remis, colis abîmé, livreur injoignable : écrivez au '
            'support, dont les coordonnées figurent dans votre profil. '
            'Indiquez la référence de la course — elle est en haut de son '
            'écran de suivi.',
      ],
      aTrancher:
          'La responsabilité en cas de perte ou de dommage, et son éventuel '
          "plafond, ne sont pas arrêtés. Aucune assurance n'est adossée aux "
          'courses aujourd\'hui.',
    ),
    SectionLegale(
      titre: 'Vos données',
      paragraphes: [
        'Ce que l\'application collecte, pourquoi, et qui peut le consulter '
            'sont décrits dans la politique de confidentialité, accessible '
            'depuis le même écran que ce document.',
      ],
    ),
    SectionLegale(
      titre: 'Modification de ces conditions',
      paragraphes: [
        'Chaque version de ce document porte un numéro. Celle que vous lisez '
            'est indiquée en haut de l\'écran.',
      ],
      aTrancher:
          'La manière dont une nouvelle version vous est présentée, et ce qui '
          "se passe si vous ne l'acceptez pas, n'est pas arrêtée. Aujourd'hui "
          "l'application ne demande aucune acceptation et n'en conserve aucune "
          'trace.',
    ),
  ],
);
