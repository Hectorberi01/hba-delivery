import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import 'tokens.dart';

/// Theme commun aux deux applications.
///
/// Plus Jakarta Sans est sous licence SIL OFL : elle peut etre embarquee dans
/// une application distribuee, contrairement a la plupart des fontes
/// geometriques commerciales qui lui ressemblent.
///
/// LE STYLE A CHANGE LE 2 OCTOBRE 2026 : surfaces blanches sur fond ivoire,
/// filets fins, terre cuite. Voir tokens.dart pour ce qui a bouge et pourquoi
/// aucun nom de jeton n'a change.
///
/// CE QUE MATERIAL APPORTE ICI, ET CE QU'IL NE DECIDE PAS. On garde de
/// Material 3 ses seuils — contraste, cible tactile, duree des transitions —
/// et sa machinerie d'etats. On lui retire tout ce qui dessine une elevation a
/// sa maniere : les ombres grises portees par `elevation`, les teintes de
/// surface (`surfaceTint`) qui eclaircissent un bloc a mesure qu'il monte. Ces
/// deux mecanismes se superposeraient a la double ombre du systeme et
/// donneraient des blocs sales, gris sur un fond chaud.
///
/// PAS DE THEME SOMBRE, ET C'EST TOUJOURS UNE DECISION. L'argument a change
/// avec le style : ce n'etait plus « le neumorphisme rend mieux en sombre »,
/// c'est qu'un second theme doublerait la surface a verifier pour une
/// condition d'usage minoritaire — ces applications se tiennent dehors, de
/// jour.
abstract final class HbaTheme {
  static ThemeData light() {
    final text = GoogleFonts.plusJakartaSansTextTheme().apply(
      bodyColor: HbaColors.ink,
      displayColor: HbaColors.ink,
    );

    return ThemeData(
      useMaterial3: true,
      scaffoldBackgroundColor: HbaColors.background,

      // LA MATIERE NE DOIT RIEN RECEVOIR D'AUTRE. Les widgets Material qui
      // posent une surface — feuilles, menus, boites de dialogue — tirent leur
      // teinte d'elevation de la couleur primaire. Mise a transparent, elle
      // cesse de bleuir les blocs a chaque cran de hauteur.
      canvasColor: HbaColors.background,

      colorScheme: const ColorScheme.light(
        // LA TERRE CUITE PROFONDE, parce que Material pose ce jeton sous du
        // blanc — barres de progression, curseur de saisie, selection — et
        // qu'une marge de contraste y vaut mieux qu'un ton juste.
        primary: HbaColors.primaryDeep,
        onPrimary: Colors.white,
        secondary: HbaColors.primary,
        onSecondary: Colors.white,
        surface: HbaColors.surface,
        onSurface: HbaColors.ink,
        surfaceContainerHighest: HbaColors.surfaceSunken,
        outline: HbaColors.border,
        error: HbaColors.danger,
        onError: Colors.white,
      ),

      textTheme: text.copyWith(
        displaySmall: text.displaySmall?.copyWith(
          fontWeight: FontWeight.w800,
          letterSpacing: -0.5,
        ),
        headlineMedium: text.headlineMedium?.copyWith(
          fontWeight: FontWeight.w800,
          letterSpacing: -0.5,
        ),
        titleLarge: text.titleLarge?.copyWith(fontWeight: FontWeight.w700),
        titleMedium: text.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        bodyMedium: text.bodyMedium?.copyWith(color: HbaColors.inkMuted, height: 1.45),

        // LE PETIT TEXTE EST EN inkMuted, PAS EN inkFaint. Il porte des dates,
        // des references, des explications de refus : des choses qu'on doit
        // pouvoir lire. inkFaint est reserve a ce qui doit justement s'effacer.
        bodySmall: text.bodySmall?.copyWith(color: HbaColors.inkMuted, height: 1.35),
        labelLarge: text.labelLarge?.copyWith(fontWeight: FontWeight.w700),
      ),

      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        foregroundColor: HbaColors.ink,
      ),

      // LE CHAMP DE SAISIE EST UN APLAT BORDE. Il etait creuse — un fond plus
      // sombre et aucun contour, qui imitait un trou dans la matiere. Les
      // maquettes le dessinent autrement : un fond a peine sourd, un filet fin
      // partout, et le filet terre cuite quand le champ a le focus.
      //
      // LE FOND RESTE PLUS SOURD QUE LE BLANC, et ce n'est pas un reste de
      // l'ancien style : c'est ce qui distingue un champ VIDE a remplir d'une
      // carte blanche a lire, avant meme qu'on lise l'etiquette.
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: HbaColors.surfaceSunken,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: HbaSpacing.md + 2,
          vertical: HbaSpacing.md,
        ),
        border: _bord(HbaColors.border),
        enabledBorder: _bord(HbaColors.border),

        // LE FOCUS GARDE UN CONTOUR FRANC. C'est le seul etat que ni l'ombre
        // ni la valeur ne savent dire, et c'est aussi celui dont depend
        // quiconque navigue au clavier ou au lecteur d'ecran.
        focusedBorder: _bord(HbaColors.primaryInk, width: 1.8),
        errorBorder: _bord(HbaColors.danger, width: 1.4),
        focusedErrorBorder: _bord(HbaColors.danger, width: 1.8),
        hintStyle: const TextStyle(color: HbaColors.inkFaint),
        prefixIconColor: HbaColors.inkMuted,
        suffixStyle: const TextStyle(color: HbaColors.inkMuted),
      ),

      dividerTheme: const DividerThemeData(
        color: HbaColors.border,
        thickness: 1,
        space: 1,
      ),

      // LA BASCULE EN LIGNE / HORS LIGNE EST LE GESTE CENTRAL DE L'APP
      // LIVREUR : sa couleur est un jeton, pas un argument pose sur un
      // widget. Le faire ici evite au passage « activeColor », deprecie
      // depuis Flutter 3.31 au profit d'un nom que les versions anterieures
      // ne connaissent pas — le theme, lui, se comporte pareil partout.
      //
      // LA PISTE EST CREUSE, LE POUCE EST EN RELIEF : c'est exactement la
      // metaphore du systeme, et c'est le seul composant ou elle est litterale.
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith(
          (etats) => etats.contains(WidgetState.selected)
              ? HbaColors.primary
              : HbaColors.surface,
        ),
        trackColor: WidgetStateProperty.resolveWith(
          (etats) => etats.contains(WidgetState.selected)
              ? HbaColors.primarySoft
              : HbaColors.surfaceSunken,
        ),
        trackOutlineColor: WidgetStateProperty.all(Colors.transparent),
        trackOutlineWidth: WidgetStateProperty.all(0),
      ),

      // LA BARRE D'ONGLETS EST POSEE SUR LA MATIERE, pas sur une surface
      // blanche : c'est la coque qui lui donne son relief, parce qu'elle seule
      // sait ou s'arrete l'ecran.
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: Colors.transparent,
        selectedItemColor: HbaColors.primaryInk,
        unselectedItemColor: HbaColors.inkMuted,
        type: BottomNavigationBarType.fixed,
        elevation: 0,
        showUnselectedLabels: true,
        selectedLabelStyle: TextStyle(fontWeight: FontWeight.w700),
      ),

      // LA FEUILLE DU BAS EST BLANCHE, ET ARRONDIE EN HAUT. C'est la forme la
      // plus presente des maquettes : la carte geographique occupe l'ecran, et
      // tout ce qui se lit ou se remplit monte depuis le bas sur une feuille
      // blanche. Elle etait ivoire, donc de la couleur du fond — elle ne
      // montait sur rien.
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: HbaColors.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(
            top: Radius.circular(HbaRadius.feuille),
          ),
        ),
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        showDragHandle: true,
        dragHandleColor: HbaColors.inkFaint,
      ),

      dialogTheme: const DialogThemeData(
        backgroundColor: HbaColors.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
      ),

      snackBarTheme: const SnackBarThemeData(
        backgroundColor: HbaColors.ink,
        contentTextStyle: TextStyle(color: Colors.white, height: 1.35),
        behavior: SnackBarBehavior.floating,
        elevation: 0,
      ),

      progressIndicatorTheme: const ProgressIndicatorThemeData(
        color: HbaColors.primaryDeep,
        linearTrackColor: HbaColors.surfaceSunken,
        circularTrackColor: Colors.transparent,
      ),

      listTileTheme: const ListTileThemeData(
        iconColor: HbaColors.inkMuted,
        textColor: HbaColors.ink,
      ),

      // Le retour tactile Material — nappe d'encre circulaire — reste etranger
      // a ce style : la pression se dit par une teinte et la disparition de
      // l'ombre (voir HbaPressable). Les rares widgets Material restants n'en
      // portent donc plus.
      splashFactory: NoSplash.splashFactory,
      highlightColor: Colors.transparent,
    );
  }

  static OutlineInputBorder _bord(Color color, {double width = 1}) =>
      OutlineInputBorder(
        borderRadius: BorderRadius.circular(HbaRadius.field),
        borderSide: color == Colors.transparent
            ? BorderSide.none
            : BorderSide(color: color, width: width),
      );
}
