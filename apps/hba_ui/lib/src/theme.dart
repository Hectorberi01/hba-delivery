import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import 'tokens.dart';

/// Theme commun aux deux applications.
///
/// Plus Jakarta Sans est sous licence SIL OFL : elle peut etre embarquee dans
/// une application distribuee, contrairement a la plupart des fontes
/// geometriques commerciales qui lui ressemblent.
///
/// CE QUE MATERIAL APPORTE ICI, ET CE QU'IL NE DECIDE PAS. On garde de
/// Material 3 ses seuils — contraste, cible tactile, duree des transitions —
/// et sa machinerie d'etats. On lui retire tout ce qui dessine une elevation a
/// sa maniere : les ombres grises portees par `elevation`, les teintes de
/// surface (`surfaceTint`) qui eclaircissent un bloc a mesure qu'il monte. Ces
/// deux mecanismes se superposeraient a la double ombre du systeme et
/// donneraient des blocs sales, gris sur un fond chaud.
///
/// PAS DE THEME SOMBRE, ET C'EST UNE DECISION. Le neumorphisme rend mieux en
/// sombre — c'est meme la qu'il est ne. Mais cette application se tient dehors,
/// de jour ; un second theme doublerait la surface a verifier pour une
/// condition d'usage minoritaire. Il demanderait aussi ses propres jetons
/// d'ombre : une ombre sombre ne s'obtient pas en inversant une ombre claire,
/// la lumiere et l'ombre ne se comportent pas symetriquement.
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
        // L'ORANGE DE REMPLISSAGE, PAS CELUI DE LA MARQUE : c'est ce jeton que
        // Material pose sous du blanc — barres de progression, curseur de
        // saisie, selection — et #F2690F n'y tiendrait pas le contraste.
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

      // LE CHAMP DE SAISIE EST UN CREUX. Faute de pouvoir peindre une ombre
      // interieure a travers InputDecoration, le creux se joue ici sur la
      // seule valeur : un fond plus SOMBRE que la page, et aucun contour. Sur
      // cette matiere, un aplat enfonce se lit deja comme un trou ; ce qui
      // trahissait l'ancien champ, c'etait son fond blanc pose sur un fond
      // creme, qui le faisait flotter.
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: HbaColors.surfaceSunken,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: HbaSpacing.md + 2,
          vertical: HbaSpacing.md,
        ),
        border: _bord(Colors.transparent),
        enabledBorder: _bord(Colors.transparent),

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

      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: HbaColors.background,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        showDragHandle: true,
        dragHandleColor: HbaColors.inkFaint,
      ),

      dialogTheme: const DialogThemeData(
        backgroundColor: HbaColors.background,
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

      // Le retour tactile Material — nappe d'encre circulaire — est etranger a
      // ce style : la pression se dit par l'enfoncement du bloc. Les rares
      // widgets Material restants n'en portent donc plus.
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
