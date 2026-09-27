import 'document_legal.dart';

/// La politique de confidentialite.
///
/// ELLE EST ECRITE DEPUIS LE CODE, PAS DEPUIS UN MODELE. Chaque phrase
/// correspond a quelque chose de verifiable dans le depot : la position part
/// toutes les vingt secondes et seulement en ligne, les pieces vivent dans un
/// stockage objet, chaque consultation d'un dossier par le back-office laisse
/// une ligne dans un journal. Un modele generique aurait promis des choses que
/// le systeme ne fait pas, et tu — le lecteur — n'aurais eu aucun moyen de le
/// savoir.
///
/// LES DUREES DE CONSERVATION N'Y FIGURENT PAS, PARCE QU'ELLES N'EXISTENT PAS.
/// Le mecanisme de purge est en place, le reglage est vide partout. Ecrire
/// « nous conservons douze mois » serait inventer une regle ; le document dit
/// donc que le point n'est pas arrete.
const confidentialite = DocumentLegal(
  titre: 'Politique de confidentialité',
  chapeau:
      'Ce que l\'application collecte, pourquoi, qui peut le consulter, et ce '
      'que vous pouvez en demander.',
  version: 'v0.2',
  miseAJour: '27 septembre 2026',
  sections: [
    SectionLegale(
      titre: 'Qui traite vos données',
      paragraphes: [
        'HBA, éditeur de cette application et de la plateforme de livraison à '
            'laquelle elle vous donne accès.',
      ],
      aTrancher:
          'La raison sociale exacte, l\'adresse du siège et le point de contact '
          'charge des données personnelles doivent figurer ici. Ils ne sont pas '
          'encore renseignés dans l\'application.',
    ),
    SectionLegale(
      titre: 'Ce que nous collectons',
      points: [
        'Votre numéro de téléphone et le nom que vous avez indique, pour vous '
            'identifier et vous joindre.',
        'Les pièces de votre dossier : pièce d\'identité, permis, carte grise, '
            'photo du véhicule, photo de profil.',
        'Votre position, uniquement lorsque vous êtes en ligne.',
        'Les courses que vous effectuez : points de collecte et de livraison, '
            'horaires, preuves de remise.',
        'Vos gains et vos demandes de versement.',
      ],
    ),
    SectionLegale(
      titre: 'Votre position',
      paragraphes: [
        'Lorsque vous passez EN LIGNE, l\'application envoie votre position '
            'toutes les vingt secondes. Lorsque vous passez hors ligne, elle '
            'cesse immédiatement de l\'envoyer.',
        'Elle continue de l\'envoyer ÉCRAN ÉTEINT et application fermée, tant '
            'que vous êtes en ligne : sans cela, HBA vous considérerait comme '
            'absent au bout de deux minutes et cesserait de vous proposer des '
            'courses. Une notification permanente vous rappelle que vous êtes '
            'en ligne tant que c\'est le cas.',
        'Deux gestes arrêtent tout : passer hors ligne, ou retirer '
            'l\'application des applications récentes.',
        'Cette position sert à une seule chose : savoir quels livreurs sont '
            'assez proches d\'un point de collecte pour qu\'une course leur soit '
            'proposée. Une position trop ancienne est ignorée — le système '
            'considère alors que le téléphone ne répond plus.',
        'Elle est conservée comme position COURANTE, remplacée à chaque envoi. '
            'L\'application ne construit pas d\'historique de vos déplacements.',
      ],
    ),
    SectionLegale(
      titre: 'Les pièces de votre dossier',
      paragraphes: [
        'Elles sont déposées depuis l\'application et rangees dans un stockage '
            'séparé de la base de données. Elles ne sont jamais visibles des '
            'clients, des commerçants, ni des autres livreurs.',
        'Seule l\'administration de HBA peut les ouvrir, pour valider ou '
            'refuser votre dossier. CHAQUE OUVERTURE EST CONSIGNÉE : qui a '
            'regarde, quand, et avec quel role. Vous consulter vous-même votre '
            'propre dossier n\'est pas consigné.',
      ],
      aTrancher:
          'La durée de conservation des pièces n\'est pas arrêtée, et leur '
          'suppression automatique n\'est pas en place. Supprimer une pièce '
          'd\'identité suppose de décider ce que devient un livreur valide dont '
          'les pièces ont disparu.',
    ),
    SectionLegale(
      titre: 'Ce que les autres voient de vous',
      points: [
        'Le client dont vous portez la course voit votre nom et votre véhicule '
            'pendant la livraison.',
        'Le personnel de HBA voit votre profil, votre état et vos courses ; '
            'seule l\'administration voit vos pièces.',
        'Les autres livreurs ne voient rien de vous.',
        'LE CODE DE REMISE NE VOUS EST JAMAIS MONTRE, et il n\'est pas montre '
            'non plus à l\'administration. Seuls le client et le destinataire '
            'le connaissent. C\'est ce qui lui donne sa valeur de preuve — pour '
            'vous autant que pour eux.',
      ],
    ),
    SectionLegale(
      titre: 'Quand l\'application plante',
      paragraphes: [
        'Si l\'application se ferme toute seule, elle envoie un rapport '
            'technique : le modèle du téléphone, sa version d\'Android ou '
            'd\'iOS, la version de l\'application, et l\'endroit du code où '
            'l\'erreur s\'est produite. Sans cela, un défaut peut durer des '
            'mois sans que personne ne le sache — vous, vous rouvrez '
            'l\'application et vous continuez.',
        'Le rapport porte VOTRE IDENTIFIANT DE LIVREUR, pour que le support '
            'retrouve vos plantages si vous appelez. Ni votre nom, ni votre '
            'téléphone, ni vos adresses, ni aucun code de remise n\'y figurent.',
        'Ces rapports sont traites par Firebase Crashlytics, un service de '
            'Google. C\'est le seul endroit où une donnée vous concernant sort '
            'des serveurs de HBA.',
      ],
    ),
    SectionLegale(
      titre: 'Combien de temps',
      paragraphes: [
        'Vos données de compte sont conservées tant que votre compte existe.',
      ],
      aTrancher:
          'Les durées de conservation ne sont pas arrêtées : ni celle des '
          'pièces du dossier, ni celle des preuves de livraison, ni celle du '
          'journal des consultations. Le mécanisme de purge existe dans le '
          'système, mais aucun délai n\'y est encore inscrit. Ce point sera '
          'tranche avant l\'ouverture du service.',
    ),
    SectionLegale(
      titre: 'Vos droits',
      paragraphes: [
        'Vous pouvez consulter votre profil et votre dossier à tout moment '
            'depuis l\'application.',
        'Vous pouvez demander la suppression de votre compte depuis votre '
            'profil. La demande part vers une personne : l\'application ne '
            'supprime pas un compte en appuyant sur un bouton, et elle ne vous '
            'le fait pas croire.',
        'Les courses déjà effectuées restent dans l\'historique de HBA, parce '
            'qu\'elles concernent aussi des clients et des commerçants et '
            'qu\'elles servent en cas de litige. Votre nom en est retiré.',
      ],
    ),
    SectionLegale(
      titre: 'Nous écrire à ce sujet',
      paragraphes: [
        'Les coordonnées du support figurent dans votre profil, section '
            'Assistance. Une demande concernant vos données personnelles passe '
            'par le même chemin.',
      ],
    ),
  ],
);
