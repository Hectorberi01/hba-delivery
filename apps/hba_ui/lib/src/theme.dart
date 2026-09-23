import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import 'tokens.dart';

/// Theme commun aux deux applications.
///
/// Plus Jakarta Sans est sous licence SIL OFL : elle peut etre embarquee dans
/// une application distribuee, contrairement a la plupart des fontes
/// geometriques commerciales qui lui ressemblent.
abstract final class HbaTheme {
  static ThemeData light() {
    final text = GoogleFonts.plusJakartaSansTextTheme().apply(
      bodyColor: HbaColors.ink,
      displayColor: HbaColors.ink,
    );

    return ThemeData(
      useMaterial3: true,
      scaffoldBackgroundColor: HbaColors.background,
      colorScheme: const ColorScheme.light(
        primary: HbaColors.primary,
        onPrimary: Colors.white,
        surface: HbaColors.surface,
        onSurface: HbaColors.ink,
        error: HbaColors.danger,
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
        titleMedium: text.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        bodyMedium: text.bodyMedium?.copyWith(color: HbaColors.inkMuted, height: 1.45),
        labelLarge: text.labelLarge?.copyWith(fontWeight: FontWeight.w700),
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        centerTitle: false,
        foregroundColor: HbaColors.ink,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: HbaColors.surface,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: HbaSpacing.md,
          vertical: HbaSpacing.md,
        ),
        border: _fieldBorder(HbaColors.border),
        enabledBorder: _fieldBorder(HbaColors.border),
        focusedBorder: _fieldBorder(HbaColors.primary, width: 1.6),
        errorBorder: _fieldBorder(HbaColors.danger),
        focusedErrorBorder: _fieldBorder(HbaColors.danger, width: 1.6),
        hintStyle: const TextStyle(color: HbaColors.inkFaint),
      ),
      dividerTheme: const DividerThemeData(
        color: HbaColors.border,
        thickness: 1,
        space: 1,
      ),
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: HbaColors.surface,
        selectedItemColor: HbaColors.primary,
        unselectedItemColor: HbaColors.inkFaint,
        type: BottomNavigationBarType.fixed,
        elevation: 0,
        showUnselectedLabels: true,
      ),
    );
  }

  static OutlineInputBorder _fieldBorder(Color color, {double width = 1}) =>
      OutlineInputBorder(
        borderRadius: BorderRadius.circular(HbaRadius.field),
        borderSide: BorderSide(color: color, width: width),
      );
}
