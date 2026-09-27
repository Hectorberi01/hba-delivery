# Regles de reduction pour la version de publication.
#
# FLUTTER ET SES GREFFONS APPORTENT LES LEURS : le greffon Gradle de Flutter
# injecte deja ce qu'il faut pour le moteur, et chaque greffon publie les
# siennes dans son AAR. Ce fichier ne contient donc que ce qui est propre a
# cette application.
#
# CE QUI SUIT PROTEGE LES CLASSES LUES PAR REFLEXION, que R8 ne voit pas
# appelees et supprimerait donc en silence. Un plantage de ce genre n'arrive
# qu'en release, sur le telephone d'un livreur, et jamais en developpement.

# Google Maps : les classes du rendu sont chargees dynamiquement.
-keep class com.google.android.gms.maps.** { *; }
-keep interface com.google.android.gms.maps.** { *; }

# Les avertissements des dependances tierces ne doivent pas faire echouer la
# construction pour des classes optionnelles jamais atteintes.
-dontwarn com.google.android.gms.**
