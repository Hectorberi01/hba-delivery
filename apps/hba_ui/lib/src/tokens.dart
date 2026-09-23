import 'package:flutter/widgets.dart';

/// Jetons de design. Aucune couleur, aucun rayon, aucune espace ne doit etre
/// ecrit en dur ailleurs : une valeur qui n'est pas ici est une valeur qui
/// divergera entre les deux applications.
abstract final class HbaColors {
  /// Orange HBA. Porte l'action principale, jamais une information.
  static const primary = Color(0xFFF2690F);
  static const primaryPressed = Color(0xFFD25708);
  static const primarySoft = Color(0xFFFFF0E5);

  /// Encre : titres et texte courant.
  static const ink = Color(0xFF141B2D);
  static const inkMuted = Color(0xFF6B7488);
  static const inkFaint = Color(0xFF9AA2B4);

  static const surface = Color(0xFFFFFFFF);
  static const background = Color(0xFFFDF8F4);
  static const border = Color(0xFFEDE7E1);

  static const success = Color(0xFF0F9D58);
  static const successSoft = Color(0xFFE7F6EE);

  static const danger = Color(0xFFD1352F);
  static const dangerSoft = Color(0xFFFCEDEC);

  static const warning = Color(0xFFB26A00);
  static const warningSoft = Color(0xFFFFF4E0);

  /// Degrade de la carte de solde, repris du style des maquettes.
  static const balanceGradient = [Color(0xFFF97B2E), Color(0xFFE24435)];
}

abstract final class HbaSpacing {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 16.0;
  static const lg = 24.0;
  static const xl = 32.0;
  static const xxl = 48.0;

  /// Marge laterale de tous les ecrans.
  static const gutter = 20.0;
}

abstract final class HbaRadius {
  static const card = 20.0;
  static const field = 16.0;
  static const button = 28.0;
  static const chip = 999.0;
}

abstract final class HbaDuration {
  static const fast = Duration(milliseconds: 150);
  static const normal = Duration(milliseconds: 250);
}
