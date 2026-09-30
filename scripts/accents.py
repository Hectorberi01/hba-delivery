#!/usr/bin/env python3
"""Cherche les chaines Dart destinees a l'ecran qui ont perdu leurs accents.

CE QUI SE DISTINGUE ICI, C'EST L'ECRAN DU COMMENTAIRE. Ce depot ecrit ses
commentaires SANS accents, deliberement, et les textes affiches AVEC. Un outil
qui confondrait les deux rendrait cent faux positifs et ne serait jamais relu.
On retire donc les commentaires d'abord, avec un automate, pas une expression
reguliere — « http:// » dans une chaine a deja fait mentir l'outil precedent.
"""
import re, sys, os

# Formes fautives frequentes -> forme correcte. La cle est le mot TEL QU'ON
# LE TROUVE quand l'accent a saute ; certaines (« a », « ou », « sur ») sont
# des mots francais valides et ne peuvent pas figurer ici sans contexte.
FAUTES = {
 "acceptee":"acceptée",
 "acces":"accès",
 "activite":"activité",
 "actualise":"actualisé",
 "actualisee":"actualisée",
 "annee":"année",
 "annees":"années",
 "annulee":"annulée",
 "annulees":"annulées",
 "apercu":"aperçu",
 "apres":"après",
 "arret":"arrêt",
 "arreter":"arrêter",
 "arrivee":"arrivée",
 "arrivees":"arrivées",
 "categorie":"catégorie",
 "categories":"catégories",
 "chargee":"chargée",
 "cle":"clé",
 "cles":"clés",
 "commandee":"commandée",
 "commandees":"commandées",
 "completee":"complétée",
 "confidentialite":"confidentialité",
 "confirmee":"confirmée",
 "connecte":"connecté",
 "connectee":"connectée",
 "controle":"contrôle",
 "controler":"contrôler",
 "cout":"coût",
 "couts":"coûts",
 "creation":"création",
 "creee":"créée",
 "creees":"créées",
 "creer":"créer",
 "creneau":"créneau",
 "creneaux":"créneaux",
 "deconnecte":"déconnecté",
 "deconnecter":"déconnecter",
 "deconnexion":"déconnexion",
 "deja":"déjà",
 "delai":"délai",
 "delais":"délais",
 "demarrage":"démarrage",
 "demarrer":"démarrer",
 "depart":"départ",
 "depassee":"dépassée",
 "derniere":"dernière",
 "dernieres":"dernières",
 "desole":"désolé",
 "desolee":"désolée",
 "detail":"détail",
 "details":"détails",
 "differee":"différée",
 "disponibilite":"disponibilité",
 "donnee":"donnée",
 "donnees":"données",
 "duree":"durée",
 "durees":"durées",
 "echeance":"échéance",
 "echeances":"échéances",
 "echec":"échec",
 "echoue":"échoué",
 "echouee":"échouée",
 "electronique":"électronique",
 "electroniques":"électroniques",
 "eloigne":"éloigné",
 "eloignee":"éloignée",
 "enregistree":"enregistrée",
 "enregistrees":"enregistrées",
 "enregistres":"enregistrés",
 "entree":"entrée",
 "entrees":"entrées",
 "envoyee":"envoyée",
 "envoyees":"envoyées",
 "estimee":"estimée",
 "etape":"étape",
 "etapes":"étapes",
 "etat":"état",
 "etats":"états",
 "ete":"été",
 "etre":"être",
 "eviter":"éviter",
 "evitez":"évitez",
 "expediteur":"expéditeur",
 "expiree":"expirée",
 "frequence":"fréquence",
 "general":"général",
 "generale":"générale",
 "generales":"générales",
 "geree":"gérée",
 "gerer":"gérer",
 "identite":"identité",
 "immediat":"immédiat",
 "immediate":"immédiate",
 "immediatement":"immédiatement",
 "incomplete":"incomplète",
 "indiquee":"indiquée",
 "interet":"intérêt",
 "itineraire":"itinéraire",
 "journee":"journée",
 "legale":"légale",
 "legaux":"légaux",
 "livree":"livrée",
 "majoree":"majorée",
 "meme":"même",
 "memes":"mêmes",
 "memoire":"mémoire",
 "modifiee":"modifiée",
 "montee":"montée",
 "necessaire":"nécessaire",
 "numerique":"numérique",
 "numero":"numéro",
 "numeros":"numéros",
 "numerote":"numéroté",
 "operateur":"opérateur",
 "operateurs":"opérateurs",
 "operation":"opération",
 "operationnel":"opérationnel",
 "operations":"opérations",
 "parametre":"paramètre",
 "parametres":"paramètres",
 "penalite":"pénalité",
 "penalites":"pénalités",
 "periode":"période",
 "periodes":"périodes",
 "precedent":"précédent",
 "precedente":"précédente",
 "precise":"précise",
 "preciser":"préciser",
 "prefere_":"préfère",
 "preferee":"préférée",
 "preleve":"prélevé",
 "prelevee":"prélevée",
 "prelevement":"prélèvement",
 "premiere":"première",
 "premieres":"premières",
 "prevu":"prévu",
 "prevue":"prévue",
 "probleme":"problème",
 "problemes":"problèmes",
 "progres":"progrès",
 "proprietaire":"propriétaire",
 "recapitulatif":"récapitulatif",
 "recu":"reçu",
 "recue":"reçue",
 "recues":"reçues",
 "recus":"reçus",
 "reduire":"réduire",
 "reduite":"réduite",
 "reel":"réel",
 "reelle":"réelle",
 "reessayer":"réessayer",
 "reessayez":"réessayez",
 "reference":"référence",
 "references":"références",
 "refusee":"refusée",
 "reglage":"réglage",
 "reglages":"réglages",
 "reglee":"réglée",
 "regles":"règles",
 "rembourse":"remboursé",
 "remboursee":"remboursée",
 "repere":"repère",
 "reperes":"repères",
 "repondre":"répondre",
 "reponse":"réponse",
 "reponses":"réponses",
 "requete":"requête",
 "requetes":"requêtes",
 "reseau":"réseau",
 "reseautage":"réseautage",
 "reseaux":"réseaux",
 "reservee":"réservée",
 "resultat":"résultat",
 "resultats":"résultats",
 "retabli":"rétabli",
 "retablie":"rétablie",
 "reussi":"réussi",
 "reussie":"réussie",
 "reussite":"réussite",
 "role":"rôle",
 "roles":"rôles",
 "securise":"sécurisé",
 "securisee":"sécurisée",
 "securite":"sécurité",
 "selectionne":"sélectionné",
 "selectionnee":"sélectionnée",
 "selectionner":"sélectionner",
 "selectionnez":"sélectionnez",
 "separee":"séparée",
 "succes":"succès",
 "supprimee":"supprimée",
 "supprimees":"supprimées",
 "systeme":"système",
 "systemes":"systèmes",
 "telechargement":"téléchargement",
 "telecharger":"télécharger",
 "telephone":"téléphone",
 "telephones":"téléphones",
 "telephonique":"téléphonique",
 "terminee":"terminée",
 "tres":"très",
 "trouvee":"trouvée",
 "validite":"validité",
 "vehicule":"véhicule",
 "vehicules":"véhicules",
 "verification":"vérification",
 "verifie":"vérifie",
 "verifiee":"vérifiée",
 "verifier":"vérifier",
 "voila":"voilà",

}

def sans_commentaires(src):
    out=[]; i=0; n=len(src)
    while i<n:
        c=src[i]
        if c=='/' and i+1<n and src[i+1]=='/':
            while i<n and src[i]!='\n': i+=1
            continue
        if c=='/' and i+1<n and src[i+1]=='*':
            i+=2
            while i+1<n and not (src[i]=='*' and src[i+1]=='/'):
                if src[i]=='\n': out.append('\n')
                i+=1
            i+=2; continue
        out.append(c); i+=1
    return ''.join(out)

CHAINE = re.compile(r"""(?<![\w$])(r?)('''|\"\"\"|'|")((?:\\.|(?!\2).)*?)\2""", re.S)

trouves=0
for racine in sys.argv[1:]:
    for dossier, _, fichiers in os.walk(racine):
        if '/.dart_tool' in dossier or '/build' in dossier: continue
        for f in sorted(fichiers):
            if not f.endswith('.dart'): continue
            chemin=os.path.join(dossier,f)
            src=open(chemin, encoding='utf-8').read()
            propre=sans_commentaires(src)
            for m in CHAINE.finditer(propre):
                texte=m.group(3)
                # DEUX TROUS CORRIGES LE 29 SEPTEMBRE 2026, ET ILS AVAIENT
                # LAISSE PASSER « Recapitulatif », « Depart » et « Arrivee »
                # dans le recapitulatif de l'envoi, vus a l'ecran par le
                # client.
                #
                # 1. « ' ' not in texte » ECARTAIT TOUT MOT SEUL. C'etait
                #    exactement la forme de la plupart des libelles d'interface
                #    — un titre, un bouton, une etiquette.
                # 2. Le motif des mots n'acceptait que des MINUSCULES :
                #    « Recapitulatif » se lisait « ecapitulatif », absent de la
                #    table. Un mot capitalise n'etait donc jamais reconnu, et
                #    un libelle commence toujours par une majuscule.
                if len(texte)<3: continue
                if texte.startswith('package:') or texte.startswith('/') or '://' in texte: continue

                # UNE CLE DE DICTIONNAIRE N'EST PAS UN TEXTE AFFICHE.
                # « json['reference'] » est du code : le signaler apprendrait a
                # ne plus lire la sortie de cet outil.
                avant = propre[m.start()-1] if m.start()>0 else ''
                apres = propre[m.end()] if m.end()<len(propre) else ''
                if avant=='[' and apres==']': continue

                # UN CHEMIN DE FICHIER N'EST PAS UNE PHRASE.
                if texte.endswith('.dart'): continue

                # UN MOT SEUL TOUT EN MINUSCULES EST UN IDENTIFIANT, pas un
                # libelle : « MarkerId('depart') », une cle de dictionnaire, un
                # nom de champ. Un texte montre au client commence par une
                # majuscule ou porte plusieurs mots.
                if ' ' not in texte.strip() and texte == texte.lower(): continue

                # LE SCANNER DE CHAINES NE SAIT PAS LIRE UNE INTERPOLATION QUI
                # CONTIENT ELLE-MEME DES GUILLEMETS — « '${n.padLeft(2, '0')}' »
                # lui fait apparier les mauvais. Le symptome est une « chaine »
                # qui traverse des lignes de code. On les ecarte, faute de
                # savoir les lire.
                if '\n' in texte and m.group(2) not in ("'''", '"""'): continue
                ligne=propre[:m.start()].count('\n')+1
                # LES INTERPOLATIONS NE SONT PAS DU FRANCAIS. « $reference »
                # est un nom de variable Dart : l'outil signalait « référence »
                # sur du code, pas sur un texte affiche.
                nu = re.sub(r"\$\{[^}]*\}", " ", texte)
                nu = re.sub(r"\$[A-Za-z_][A-Za-z0-9_]*", " ", nu)
                # LES CAPITALES NE PORTENT PAS D'ACCENT, PAR CONVENTION, et ce
                # depot s'en sert pour l'emphase au milieu d'une phrase — « LE
                # NUMERO DE VOTRE COMPTE ». Le tri se fait MOT PAR MOT : ecarter
                # la chaine entiere laisserait passer le reste de la phrase.
                mots=[w for w in re.findall(r"[A-Za-zÀ-ÖØ-öø-ÿœŒ]{2,}", nu)
                      if w != w.upper()]
                fautifs=sorted({FAUTES[w.lower()] for w in mots if w.lower() in FAUTES})
                if fautifs:
                    trouves+=1
                    print(f"{chemin}:{ligne}")
                    print(f"    « {texte[:100]} »")
                    print(f"    -> {', '.join(fautifs)}")
print(f"\n{trouves} chaine(s) suspecte(s).")
