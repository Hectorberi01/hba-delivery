import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:url_launcher/url_launcher.dart';

import '../legal/conditions_client.dart';
import '../legal/confidentialite_client.dart';
import 'reglages_client.dart';
import 'tuile.dart';

/// Comment joindre HBA, et les documents legaux.
///
/// PARTAGE ENTRE LE PROFIL ET L'AIDE, parce que le client cherche le support
/// aux deux endroits et qu'aucun des deux n'est le mauvais. Le dupliquer ferait
/// diverger les deux le jour ou l'un change.
///
/// RIEN N'EST AFFICHE QUI N'OUVRE RIEN. Chaque entree dependant d'un reglage de
/// compilation, un deploiement sans numero de support n'affiche pas de bouton
/// « Appeler » — il affiche la raison. Voir [Reglages].
class BlocSupport extends StatelessWidget {
  const BlocSupport({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const HbaSection('Aide & assistance'),

        if (!Reglages.aSupport)
          const HbaTuile(
            icone: Icons.support_agent_outlined,
            titre: 'Contacter le support',
            detail: "Aucun canal n'est configuré dans cette version. Un bouton "
                "qui n'ouvre rien serait pire que pas de bouton.",
          ),

        if (Reglages.supportTelephone.isNotEmpty) ...[
          const _Entree(
            icone: Icons.call_outlined,
            libelle: 'Appeler le support',
            detail: Reglages.supportTelephone,
            // « tel: » OUVRE LE COMPOSEUR, IL N'APPELLE PAS. Android et iOS
            // exigent tous deux que la personne confirme ; aucune application
            // ne declenche un appel toute seule.
            cible: 'tel:${Reglages.supportTelephone}',
          ),
          const SizedBox(height: HbaSpacing.sm),
        ],

        if (Reglages.supportEmail.isNotEmpty)
          _Entree(
            icone: Icons.mail_outline,
            libelle: 'Écrire au support',
            detail: Reglages.supportEmail,
            cible: courrielSupport(),
          ),

        const SizedBox(height: HbaSpacing.lg),
        const HbaSection('Documents légaux'),

        // LES TEXTES VIVENT DANS L'APPLICATION, PAS SUR UN SITE.
        //
        // Un client lit ses conditions la ou le reseau est mauvais ; une page
        // web change sans que personne ne sache quelle version il avait sous
        // les yeux ; et une application qui renvoie vers un site n'a plus
        // aucune condition le jour ou le site tombe. L'adresse en ligne reste
        // possible EN PLUS — les deux magasins l'exigent dans la fiche du
        // produit — mais elle ne remplace pas l'ecran.
        const _Document(
          icone: Icons.description_outlined,
          teinte: Color(0xFFFCEADC),
          couleurIcone: HbaColors.primary,
          document: conditionsClient,
          enLigne: Reglages.conditions,
        ),
        const SizedBox(height: HbaSpacing.sm),
        const _Document(
          icone: Icons.shield_outlined,
          teinte: Color(0xFFFBEFDC),
          couleurIcone: HbaColors.warning,
          document: confidentialiteClient,
          enLigne: Reglages.confidentialite,
        ),
      ],
    );
  }
}

/// Une ligne d'aide portant sur UNE course.
///
/// ELLE NE VIT PAS DANS [BlocSupport], ET C'EST LE POINT. Ce bloc-la porte les
/// deux contacts ET les documents legaux : le poser sur un ecran de suivi y
/// mettrait les conditions generales entre le code de remise et le bouton
/// d'annulation. Ce qui se partage, c'est [courrielSupport], pas la mise en
/// page.
///
/// LA REFERENCE PART DANS L'OBJET, ET C'EST TOUTE SA RAISON D'ETRE. Un client
/// qui ecrit depuis son suivi n'a pas a recopier « HBA-4F2A1C » a la main — il
/// le recopierait de travers une fois sur cinq, et le support repondrait sur la
/// mauvaise course.
///
/// RIEN NE S'AFFICHE SANS ADRESSE CONFIGUREE. Meme regle que partout ailleurs :
/// un bouton qui n'ouvre rien est pire que pas de bouton.
class AideSurLaCourse extends StatelessWidget {
  const AideSurLaCourse({required this.reference, super.key});

  final String reference;

  @override
  Widget build(BuildContext context) {
    if (Reglages.supportEmail.isEmpty) return const SizedBox.shrink();

    return _Entree(
      icone: Icons.help_outline,
      libelle: 'Un problème sur cette course ?',
      detail: 'Écrire au support — la référence $reference sera jointe',
      cible: courrielSupport(reference: reference, sujet: 'Course'),
    );
  }
}

class _Entree extends StatelessWidget {
  const _Entree({
    required this.icone,
    required this.libelle,
    required this.detail,
    required this.cible,
  });

  final IconData icone;
  final String libelle;
  final String detail;
  final String cible;

  @override
  Widget build(BuildContext context) => HbaTuile(
        icone: icone,
        teinte: HbaColors.primarySoft,
        couleurIcone: HbaColors.primary,
        titre: libelle,
        detail: detail,
        onTap: () => _ouvrir(context),
      );

  /// L'ECHEC SE DIT, IL NE SE TAIT PAS.
  ///
  /// Sur Android 11 et au-dela, une application ne VOIT le composeur ou le
  /// client de messagerie que si elle les a declares dans « queries ». Sans la
  /// declaration, l'appel part, ne leve aucune erreur, et ne produit rien. Un
  /// client qui appuie trois fois sur « Appeler le support » sans reaction
  /// conclut que l'application est cassee — et il a raison, mais pas sur la
  /// cause.
  Future<void> _ouvrir(BuildContext context) async {
    final uri = Uri.tryParse(cible);

    if (uri != null) {
      try {
        if (await launchUrl(uri, mode: LaunchMode.externalApplication)) return;
      } on Object {
        // On tombe sur le message ci-dessous.
      }
    }

    if (!context.mounted) return;

    // LE MESSAGE DIT LE CONTACT, PAS LE LIBELLE DE LA LIGNE. Il se construisait
    // sur « detail », qui n'est un numero ou une adresse que pour deux des
    // trois entrees : la troisieme aurait donne « Impossible d'ouvrir Ecrire au
    // support — la reference HBA-4F2A1C sera jointe. » Or c'est precisement au
    // moment ou rien ne s'ouvre que le client a besoin de LIRE le contact pour
    // le composer lui-meme.
    final quoi = cible.startsWith('tel:') ? 'le composeur' : 'votre messagerie';
    final contact = uri?.path ?? detail;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text("Impossible d'ouvrir $quoi. Notez : $contact")),
    );
  }
}

/// Une entree qui ouvre un document juridique embarque.
///
/// ELLE N'EST JAMAIS DESACTIVEE, contrairement aux entrees de support : le
/// texte est dans le binaire, il s'ouvre toujours. C'est justement pourquoi il
/// y vit.
/// Une entree qui ouvre un document juridique embarque.
///
/// ELLE N'EST JAMAIS DESACTIVEE, contrairement aux entrees de support : le
/// texte est dans le binaire, il s'ouvre toujours. C'est justement pourquoi il
/// y vit.
class _Document extends StatelessWidget {
  const _Document({
    required this.icone,
    required this.teinte,
    required this.couleurIcone,
    required this.document,
    required this.enLigne,
  });

  final IconData icone;
  final Color teinte;
  final Color couleurIcone;
  final DocumentLegal document;
  final String enLigne;

  @override
  Widget build(BuildContext context) => HbaTuile(
        icone: icone,
        teinte: teinte,
        couleurIcone: couleurIcone,
        titre: document.titre,
        // LE NUMERO DE VERSION EST AFFICHE DES LA LISTE. Le jour ou un client
        // contestera une regle, la question sera de savoir quelle version il
        // avait lue.
        detail: '${document.version} — ${document.miseAJour}',
        onTap: () => Navigator.of(context).push(
          MaterialPageRoute(
            builder: (_) => DocumentScreen(
              document: document,
              enLigne: enLigne.isEmpty ? null : enLigne,
            ),
          ),
        ),
      );
}
