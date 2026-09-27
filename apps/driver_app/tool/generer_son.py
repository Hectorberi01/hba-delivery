#!/usr/bin/env python3
"""Genere assets/sons/offre.wav.

POURQUOI UN SCRIPT PLUTOT QU'UN FICHIER TELECHARGE. Un binaire dont personne ne
sait d'ou il vient est un binaire qu'on n'ose plus toucher : on ignore sa
licence, on ignore comment le regenerer plus fort ou plus court, et on finit
par vivre avec. Ici, le son EST ce script — quatre lignes a changer pour le
rendre plus grave, plus long ou plus insistant.

    python3 tool/generer_son.py

Aucune dependance : « wave » et « struct » sont dans la bibliotheque standard.
"""

import math
import struct
import wave
from pathlib import Path

TAUX = 44100          # 44,1 kHz : lu sans reechantillonnage par tous les telephones
AMPLITUDE = 0.55      # de la marge sous la saturation, pour eviter le craquement


def note(frequence: float, duree: float) -> list[float]:
    """Une note avec attaque et extinction douces.

    L'ENVELOPPE N'EST PAS DU CONFORT : une sinusoide qui commence et s'arrete
    net produit un « clac » a chaque bout — c'est la discontinuite qu'on
    entend, pas le son. Douze millisecondes de montee et cent de descente
    suffisent a la faire disparaitre.
    """
    n = int(TAUX * duree)
    attaque = int(TAUX * 0.012)
    extinction = int(TAUX * 0.10)
    sortie = []

    for i in range(n):
        if i < attaque:
            enveloppe = i / attaque
        elif i > n - extinction:
            enveloppe = max(0.0, (n - i) / extinction)
        else:
            enveloppe = 1.0

        # Une quinte juste au-dessus, tres en retrait : elle donne au timbre de
        # quoi percer le bruit de la rue sans devenir strident.
        onde = (
            math.sin(2 * math.pi * frequence * i / TAUX)
            + 0.22 * math.sin(2 * math.pi * frequence * 1.5 * i / TAUX)
        )
        sortie.append(AMPLITUDE * enveloppe * onde / 1.22)

    return sortie


def silence(duree: float) -> list[float]:
    return [0.0] * int(TAUX * duree)


def main() -> None:
    # Deux notes MONTANTES : « quelque chose arrive ». Descendantes, elles
    # diraient « quelque chose se termine ». Le motif est joue deux fois, parce
    # qu'une seule occurrence se confond avec une notification quelconque.
    motif = note(880, 0.16) + silence(0.04) + note(1320, 0.26) + silence(0.12)
    echantillons = motif + motif

    cible = Path(__file__).resolve().parent.parent / 'assets' / 'sons' / 'offre.wav'
    cible.parent.mkdir(parents=True, exist_ok=True)

    with wave.open(str(cible), 'w') as fichier:
        fichier.setnchannels(1)
        fichier.setsampwidth(2)
        fichier.setframerate(TAUX)
        fichier.writeframes(b''.join(
            struct.pack('<h', max(-32767, min(32767, int(e * 32767))))
            for e in echantillons
        ))

    print(f'{cible} : {cible.stat().st_size} octets, '
          f'{len(echantillons) / TAUX:.2f} s')


if __name__ == '__main__':
    main()
