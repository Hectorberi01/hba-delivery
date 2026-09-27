import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../tokens.dart';
import 'hba_relief.dart';

/// Confirmation par GLISSEMENT, de la gauche vers la droite.
///
/// POURQUOI PAS UN BOUTON. Les gestes de cet ecran engagent le livreur devant
/// un client : accepter une course, annoncer qu'on est sur place, declarer le
/// colis recupere. Un appui se declenche par accident — telephone dans la
/// poche, gant humide, secousse sur une piste. Un glissement de trois
/// centimetres ne se produit pas tout seul, et il ne demande pas pour autant
/// de viser : c'est le geste le plus sur qu'on puisse faire en roulant.
///
/// LA FRICTION EST LE SUJET, PAS UN ORNEMENT. Tout ce qui est reversible reste
/// un bouton ordinaire — « Refuser », « Terminer », « Reessayer ». Le
/// glissement est reserve a ce qui part vers le serveur et change l'etat d'une
/// course.
///
/// LE SEUIL EST A 70 % DE LA COURSE, PAS A 100 %. Demander le bout exact du
/// rail oblige a un geste parfait, pouce tendu, souvent a une main : le
/// livreur s'arrete a quatre-vingt-dix pour cent et le rail revient en
/// arriere, ce qui se lit comme une panne. Au-dela du seuil, le pouce est
/// lache et le curseur finit la course tout seul. Une detente rapide
/// (« fling ») confirme aussi, plus tot : c'est le geste de quelqu'un qui sait
/// deja ce qu'il fait.
class HbaGlissiere extends StatefulWidget {
  const HbaGlissiere({
    required this.libelle,
    required this.onConfirme,
    this.libelleConfirme,
    this.busy = false,
    super.key,
  });

  /// Ce que le glissement declenche. Null desactive le rail.
  final VoidCallback? onConfirme;

  final String libelle;

  /// Affiche pendant l'aller-retour reseau. Par defaut, [libelle].
  final String? libelleConfirme;

  final bool busy;

  @override
  State<HbaGlissiere> createState() => _HbaGlissiereState();
}

class _HbaGlissiereState extends State<HbaGlissiere>
    with SingleTickerProviderStateMixin {
  static const _hauteur = 58.0;
  static const _curseur = 50.0;
  static const _marge = 4.0;

  /// Part de la course au-dela de laquelle on lache : voir l'en-tete.
  static const _seuil = 0.70;

  /// Vitesse a partir de laquelle une detente confirme sans atteindre le
  /// seuil, en pixels par seconde.
  static const _detente = 900.0;

  late final AnimationController _ressort = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 220),
  )..addListener(() => setState(() => _part = _ressort.value));

  /// Position du curseur, de 0 (au repos) a 1 (au bout).
  double _part = 0;

  bool _confirme = false;

  @override
  void didUpdateWidget(covariant HbaGlissiere ancien) {
    super.didUpdateWidget(ancien);

    // LE RAIL REVIENT QUAND L'ACTION EST FINIE, PAS AVANT. Le ramener des
    // l'appel laisserait croire que rien n'est parti ; le garder au bout une
    // fois la reponse arrivee laisserait un rail bloque sous les yeux du
    // livreur si l'ecran ne change pas — un refus du serveur, par exemple.
    if (ancien.busy && !widget.busy && _confirme) {
      _confirme = false;
      _revenir();
    }
  }

  @override
  void dispose() {
    _ressort.dispose();
    super.dispose();
  }

  void _revenir() {
    _ressort
      ..value = _part
      ..animateTo(0, curve: Curves.easeOutCubic);
  }

  void _terminer(double course) {
    _confirme = true;
    _ressort
      ..value = _part
      ..animateTo(1, curve: Curves.easeOutCubic);

    // Le meme retour tactile que celui d'un interrupteur bascule : le livreur
    // sait que c'est parti sans avoir a relire l'ecran.
    _haptique();
    widget.onConfirme?.call();

    // FILET : TOUTE ACTION NE PASSE PAS PAR « busy ». Celle qui ouvre une
    // feuille — la saisie du code de remise — ne leve aucun indicateur tant
    // que le livreur n'a pas saisi son code, et il peut l'annuler. Sans ce
    // retour differe, le rail resterait bloque au bout, sans moyen de le
    // ramener.
    Future<void>.delayed(const Duration(milliseconds: 900), () {
      if (!mounted || widget.busy || !_confirme) return;
      _confirme = false;
      _revenir();
    });
  }

  void _haptique() {
    try {
      HapticFeedback.mediumImpact();
    } on Object {
      // Telephone sans retour tactile : le mouvement suffit.
    }
  }

  @override
  Widget build(BuildContext context) {
    final actif = widget.onConfirme != null && !widget.busy && !_confirme;

    return LayoutBuilder(
      builder: (context, contraintes) {
        // La course utile : la largeur du rail moins le curseur et ses marges.
        final course =
            (contraintes.maxWidth - _curseur - _marge * 2).clamp(1.0, 4000.0);

        return Semantics(
          button: true,
          enabled: widget.onConfirme != null,
          label: widget.libelle,

          // LE GLISSEMENT N'EST PAS LE SEUL CHEMIN. Un lecteur d'ecran active
          // ceci par un double appui : exiger le geste reviendrait a fermer
          // l'application a qui ne peut pas le faire.
          onTap: actif ? () => _terminer(course) : null,
          child: ExcludeSemantics(
            child: SizedBox(
              height: _hauteur,
              child: Stack(
                children: [
                  Positioned.fill(
                    child: HbaCreux(
                      radius: _hauteur / 2,
                      color: actif || widget.busy
                          ? HbaColors.surfaceSunken
                          : HbaColors.background,
                      child: Center(
                        child: Opacity(
                          // Le libelle s'efface a mesure que le curseur
                          // avance : passe la moitie du rail, il serait de
                          // toute facon recouvert.
                          opacity: (1 - _part * 1.6).clamp(0.0, 1.0),
                          child: Padding(
                            // La reserve de gauche est la place du curseur au
                            // repos ; celle de droite empeche un libelle long
                            // de venir toucher le bord du rail.
                            padding: const EdgeInsets.only(
                              left: _curseur,
                              right: HbaSpacing.md,
                            ),
                            child: Text(
                              widget.busy
                                  ? widget.libelleConfirme ?? widget.libelle
                                  : widget.libelle,
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                color: actif || widget.busy
                                    ? HbaColors.ink
                                    : HbaColors.inkFaint,
                                fontWeight: FontWeight.w700,
                                fontSize: 15,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                    left: _marge + _part * course,
                    top: _marge,
                    child: GestureDetector(
                      behavior: HitTestBehavior.opaque,
                      onHorizontalDragUpdate: actif
                          ? (details) => setState(() => _part =
                              (_part + details.delta.dx / course)
                                  .clamp(0.0, 1.0))
                          : null,
                      onHorizontalDragEnd: actif
                          ? (details) {
                              final lance =
                                  details.velocity.pixelsPerSecond.dx >=
                                      _detente;

                              if (_part >= _seuil || (lance && _part > 0.15)) {
                                _terminer(course);
                              } else {
                                _revenir();
                              }
                            }
                          : null,
                      child: _Curseur(
                        actif: actif || widget.busy,
                        busy: widget.busy,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

class _Curseur extends StatelessWidget {
  const _Curseur({required this.actif, required this.busy});

  final bool actif;
  final bool busy;

  @override
  Widget build(BuildContext context) => Container(
        height: _HbaGlissiereState._curseur,
        width: _HbaGlissiereState._curseur,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: actif
              ? const LinearGradient(
                  colors: HbaColors.balanceGradient,
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                )
              : null,
          color: actif ? null : HbaColors.surface,
          boxShadow: HbaOmbres.relief(HbaElevation.normale),
        ),
        child: busy
            ? const Padding(
                padding: EdgeInsets.all(15),
                child: CircularProgressIndicator(
                  strokeWidth: 2.4,
                  valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
                ),
              )
            : Icon(
                Icons.arrow_forward_rounded,
                color: actif ? Colors.white : HbaColors.inkFaint,
                size: 22,
              ),
      );
}
