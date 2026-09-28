import 'package:hba_ui/hba_ui.dart';

/// Les conditions du livreur.
///
/// CE TEXTE DECRIT CE QUE LE SYSTEME FAIT, PAS CE QU'ON VOUDRAIT QU'IL FASSE.
/// Chaque regle enoncee ici correspond a un controle qui existe dans le code :
/// une offre expire, un code de remise est exige, une seule demande de
/// versement a la fois. Les points ou le systeme ne tranche rien sont ecrits
/// comme tels plutot que combles par une formule.
const conditionsLivreur = DocumentLegal(
  titre: 'Conditions du livreur',
  chapeau:
      'Ce que vous pouvez attendre de HBA, et ce que HBA attend de vous. Ces '
      'conditions décrivent le fonctionnement de l\'application telle qu\'elle '
      'est aujourd\'hui.',
  version: 'v0.1',
  miseAJour: '27 septembre 2026',
  sections: [
    SectionLegale(
      titre: 'Le role de HBA',
      paragraphes: [
        'HBA met en relation des clients, des commerçants et des livreurs. '
            'L\'application vous propose des courses, suit leur déroulement et '
            'tient le compte de ce qui vous est du.',
        'HBA ne conduit pas à votre place et ne choisit pas votre itinéraire : '
            'le guidage passe par l\'application de navigation de votre choix.',
      ],
      aTrancher:
          'La nature du lien entre HBA et le livreur — indépendant, prestataire, '
          'ou salarié — n\'est pas arrêtée. Elle a des conséquences bien au-delà '
          'de l\'application, et sera précisée dans la version definitive.',
    ),
    SectionLegale(
      titre: 'Devenir livreur',
      paragraphes: [
        'Vous créez votre compte avec votre numéro de téléphone. Vous déposez '
            'ensuite les pièces de votre dossier depuis l\'application.',
        'HBA valide ou refuse le dossier. Tant qu\'il n\'est pas valide, vous '
            'pouvez ouvrir l\'application mais aucune course ne vous est '
            'proposée. Un refus est toujours motivé : le motif s\'affiche dans '
            'votre profil, et il vous dit quoi corriger.',
      ],
    ),
    SectionLegale(
      titre: 'Les offres de course',
      paragraphes: [
        'Une course vous est PROPOSÉE, jamais imposée. Vous disposez d\'un '
            'délai pour répondre ; passe ce délai, l\'offre est proposée à un '
            'autre livreur.',
        'Une offre ne vous parvient que si vous êtes en ligne, que votre '
            'dossier est valide et que votre téléphone a donné sa position '
            'récemment. C\'est la raison pour laquelle l\'application partage '
            'votre position toutes les vingt secondes lorsque vous êtes en '
            'ligne, et jamais lorsque vous ne l\'êtes pas.',
        'Refuser une offre n\'entraine aujourd\'hui aucune conséquence dans le '
            'système : rien n\'est compte, rien n\'est pénalise.',
      ],
    ),
    SectionLegale(
      titre: 'Le déroulement d\'une course',
      points: [
        'Vous vous rendez au point de collecte et signalez votre arrivée.',
        'Vous prenez le colis en charge.',
        'À la livraison, le destinataire vous dicte un code à six chiffres. '
            'Vous ne recevez jamais ce code : c\'est lui qui prouve que le '
            'colis a bien été remis, et non simplement qu\'un bouton a été '
            'presse.',
      ],
      aTrancher:
          'Ce qui se passe lorsqu\'une course commencée doit être annulee — par '
          'vous, par le client, ou par HBA — n\'est pas arrêté. Aucune règle '
          'd\'annulation n\'est aujourd\'hui appliquee par le système.',
    ),
    SectionLegale(
      titre: 'Votre rémunération',
      paragraphes: [
        'Le montant qui vous revient est FIGÉ au moment où la course est '
            'confirmee, et c\'est ce montant que vous voyez sur l\'offre. Il ne '
            'change pas si la grille tarifaire evolue entre-temps.',
        'Ce montant est votre NET : aucune commission n\'en est retirée. La '
            'part de HBA est l\'écart entre ce que paie le client et ce qui '
            'vous revient, et elle est déjà prise dans le prix annonce au '
            'client.',
        'Une course livrée crédite votre compte. L\'écran Gains vous montre à '
            'tout moment ce qui a été gagné, ce qui a déjà été versé, et ce qui '
            'reste du.',
      ],
    ),
    SectionLegale(
      titre: 'Se faire verser',
      points: [
        'Vous demandez un versement du montant de votre choix, dans la limite '
            'de ce qui vous est du.',
        'Une seule demande à la fois. La suivante devient possible quand la '
            'précédente est soldée.',
        'HBA approuve ou refuse. Un refus est toujours motivé, et le motif '
            's\'affiche dans votre application.',
        'L\'approbation N\'EST PAS le versement : le virement se fait ensuite '
            'chez l\'opérateur. Votre solde ne baisse qu\'au moment où la '
            'référence du virement est enregistrée — c\'est-à-dire quand '
            'l\'argent est parti.',
      ],
    ),
    SectionLegale(
      titre: 'Suspension et fin de compte',
      paragraphes: [
        'HBA peut suspendre un compte. Une suspension est motivée, et le motif '
            's\'affiche dans votre profil.',
        'Vous pouvez demander la suppression de votre compte depuis votre '
            'profil. Les courses déjà effectuées restent dans l\'historique de '
            'HBA — elles concernent aussi des clients et des commerçants — mais '
            'votre nom en est retiré.',
      ],
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
          'se passe si vous ne l\'acceptez pas, n\'est pas arrêtée. Aujourd\'hui '
          'l\'application ne demande aucune acceptation et n\'en conserve aucune '
          'trace.',
    ),
  ],
);
