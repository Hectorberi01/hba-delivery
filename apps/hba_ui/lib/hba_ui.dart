/// Socle visuel commun aux applications HBA.
library;

export 'src/carte_style.dart';

// LE CADRE D'UN DOCUMENT JURIDIQUE EST COMMUN, LES TEXTES NE LE SONT PAS.
// La forme — chapeau, sections, version, bandeau « brouillon » — vaut pour le
// livreur comme pour le client ; ce qui est ecrit dedans ne se partage pas.
// Vivait dans driver_app, ou l'app cliente ne pouvait pas l'atteindre.
export 'src/legal/document_legal.dart';
export 'src/legal/document_screen.dart';
export 'src/money.dart';
export 'src/theme.dart';
export 'src/tokens.dart';
export 'src/widgets/hba_arc_nav.dart';
export 'src/widgets/hba_button.dart';
export 'src/widgets/hba_card.dart';
export 'src/widgets/hba_chip.dart';
export 'src/widgets/hba_glissiere.dart';
export 'src/widgets/hba_relief.dart';
