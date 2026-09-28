# Raccourcis du quotidien. `make` seul affiche l'aide.

.DEFAULT_GOAL := help
.PHONY: chaines repartir trace fiche fedapay webhook rebuild terrain offre clients versements carte dossier garage-init kyc etape7 help net up down logs migrate identity-key tunnel tunnel-stop topics build services-up test test-domain test-arch test-e2e migrations lint-proto lint-compose infra-up prod-up prod-service clean

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

net: ## Crée le réseau partagé hba-internal (idempotent)
# Le reseau est cree hors compose et declare « external » partout : c'est ce qui
# fait que toutes les piles s'y rattachent au lieu d'en creer chacune un. Sans
# lui, compose echoue en disant que le reseau declare externe n'existe pas.
	@docker network inspect hba-internal >/dev/null 2>&1 \
		|| docker network create hba-internal

up: net ## Démarre les dépendances locales
	docker compose -f deploy/docker-compose.yml up -d

topics: ## (Re)crée les topics Kafka
# kafka-init est un conteneur a usage unique : une fois sorti, « up » ne le
# relance pas. S'il a echoue au premier demarrage — broker pas encore pret,
# volume recree —, les topics n'existent jamais et les services echouent sur
# « Unknown topic or partition », ce qui ne designe pas la cause.
# Cette cible le rejoue explicitement. Le script est idempotent
# (--if-not-exists), donc elle se relance sans risque.
	docker compose -f deploy/docker-compose.yml run --rm kafka-init

down: ## Arrête les dépendances locales
	docker compose -f deploy/docker-compose.yml down

logs: ## Suit les journaux des dépendances
	docker compose -f deploy/docker-compose.yml logs -f

# Chaque service est un projet compose autonome, décrit dans son propre dossier.
SERVICE_DIRS := \
	src/Services/Identity src/Services/Directory src/Services/Delivery \
	src/Services/Pricing src/Services/Dispatch src/Services/Driver \
	src/Services/Payment src/Services/Notification src/Services/Media \
	src/Gateways/Hba.Gateway

lint-compose: ## Valide chaque compose, infrastructure comprise
# Les variables sensibles utilisent la forme requise : sans valeur, « config » echoue.
# deploy/lint.env fournit des valeurs factices, chargees dans l'environnement du
# shell plutot que via --env-file, dont la resolution de chemin relatif depend du
# repertoire de projet deduit du premier -f. L'environnement du processus, lui,
# ne depend de rien.
	@set -a; . ./deploy/lint.env; set +a; \
	docker compose -f deploy/compose.infra.yml config -q || exit 1; \
	for d in $(SERVICE_DIRS); do \
		docker compose -f $$d/docker-compose.yml config -q || exit 1; \
	done
	@echo "Tous les fichiers compose sont valides."

infra-up: net ## Démarre l'infrastructure partagée
	docker compose -f deploy/compose.infra.yml up -d

services-up: net ## Démarre les douze services, SANS toucher à l'infrastructure
# Separe de infra-up a dessein. Depuis que tout partage le reseau hba-internal,
# les services conteneurises fonctionnent aussi bien contre l'infra de
# developpement (« make up », qui PUBLIE 5432) que contre celle de production
# (« make infra-up », qui ne publie rien). En local on veut la premiere, sinon
# ni psql ni « make migrate » ne peuvent atteindre la base depuis le Mac.
	@for d in $(SERVICE_DIRS); do \
		echo "-- $$d"; \
		docker compose -f $$d/docker-compose.yml up -d || exit 1; \
	done

prod-up: infra-up services-up ## Infrastructure de production puis les douze services

prod-service: net ## Démarre un seul service : make prod-service D=src/Services/Identity
	@test -n "$(D)" || (echo "Indiquez le dossier : make prod-service D=src/Services/Identity" && exit 1)
	docker compose -f $(D)/docker-compose.yml up -d

rebuild: net ## Reconstruit et relance un service : make rebuild D=src/Gateways/Hba.Gateway
# DEUX DRAPEAUX, ET LES DEUX SERVENT.
#
# « --build » reprend le code : sans lui, on relance fidelement l'ANCIENNE
# image, et le service redemarre avec le code d'avant. C'est la panne la plus
# deroutante qui soit, parce que la modification est bien dans le fichier.
#
# « --force-recreate » fait relire le .env, qui n'est lu qu'a la CREATION du
# conteneur : un simple redemarrage garde les anciennes variables.
#
# SE LANCE DEPUIS LA RACINE, et c'est tout l'interet. Les instructions en deux
# lignes — un « cd » puis une commande — se collent a moitie, et « docker
# compose » repond alors « no configuration file provided » depuis un
# repertoire qui n'en a pas.
	@test -n "$(D)" || (echo "Indiquez le dossier : make rebuild D=src/Gateways/Hba.Gateway" && exit 1)
	@test -f "$(D)/docker-compose.yml" || (echo "Pas de docker-compose.yml dans $(D)" && exit 1)
	docker compose -f $(D)/docker-compose.yml up -d --build --force-recreate

build: ## Compile toute la solution
	dotnet build HbaDelivery.sln

test: ## Lance tous les tests
	dotnet test HbaDelivery.sln

test-domain: ## Tests du domaine Delivery, sans infrastructure
	dotnet test src/Services/Delivery/tests/Hba.Delivery.Domain.Tests/Hba.Delivery.Domain.Tests.csproj

test-arch: ## Règles de dépendance entre couches
	dotnet test tests/Architecture.Tests/Architecture.Tests.csproj

test-e2e: ## Parcours d'inscription de bout en bout (Docker requis)
	dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj

migrate: ## Applique les migrations à la base de développement
# En Production, Database:AutoMigrate vaut false : un service ne touche pas au
# schema au demarrage. Les migrations sont donc appliquees explicitement, ici.
	./scripts/apply-migrations.sh

identity-key: ## Pose la clé de signature d'Identity dans son volume, si elle manque
# SANS CETTE CLE, IDENTITY DEMARRE MAIS NE PEUT EMETTRE AUCUN JETON. Hors
# developpement, JwtSigning:AllowEphemeralKey vaut false : le service refuse de
# generer une cle au demarrage, parce qu'une cle ephemere invaliderait tous les
# jetons a chaque redemarrage et differerait d'une instance a l'autre. Il refuse
# donc, mais TARD : le fournisseur est un singleton, construit a la premiere
# emission de jeton. Le service repond a /health, sert le JWKS, accepte les
# demandes de code — et s'effondre a la premiere connexion d'un utilisateur.
#
# Le volume identity-keys est vide sur une installation neuve. Cette cible le
# remplit une fois pour toutes ; la cle y survit aux redemarrages, et les jetons
# deja emis restent valides.
	@set -e; \
	C="docker compose -f src/Services/Identity/docker-compose.yml"; \
	if $$C exec -T identity test -f /var/hba/keys/identity-signing.pem 2>/dev/null; then \
		echo "Cle de signature deja en place, rien a faire."; \
	else \
		command -v openssl >/dev/null || { echo "openssl introuvable." >&2; exit 1; }; \
		tmp=$$(mktemp); \
		openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out $$tmp 2>/dev/null; \
		$$C cp $$tmp identity:/var/hba/keys/identity-signing.pem; \
		rm -f $$tmp; \
		: "docker cp depose le fichier en root, et openssl le cree souvent en 0600 :"; \
		: "le conteneur tourne sous hba (uid 10001) et ne pourrait pas le lire."; \
		$$C exec -T -u root identity chmod 0644 /var/hba/keys/identity-signing.pem; \
		$$C restart identity; \
		echo "Cle generee, deposee dans le volume identity-keys, Identity redemarre."; \
	fi

tunnel: ## Expose la gateway via ngrok et cable FedaPay dessus (NGROK_DOMAIN=… optionnel)
# Sans adresse publique, FedaPay ne peut ni joindre le webhook, ni renvoyer le
# payeur. L'echec est muet : le client paie, la livraison reste en
# PENDING_PAYMENT, aucun journal ne signale rien.
	./scripts/tunnel.sh

etape4: ## Exerce la boucle complete : paiement, dispatch, offre, affectation
# Pose deux surcouches de developpement (OTP fixe, paiement factice) et les
# retire a la sortie, y compris en cas d'echec.
	./scripts/etape4-boucle.sh

garage-init: ## Initialise Garage : disposition, bucket, cle d'acces (idempotent)
# GARAGE NE SERT RIEN AVANT CETTE ETAPE. Il repond « no storage nodes » a la
# moindre ecriture tant qu'aucune disposition n'est appliquee — la difference
# qui surprend quand on vient de MinIO.
#
# Le script tourne sur l'hote : l'image de Garage ne contient que le binaire,
# sans shell, donc rien d'autre ne s'y execute.
	./scripts/garage-init.sh

kyc: ## Valide le dossier d'un livreur : make kyc Q=+33618710738 (sans Q, liste)
	./scripts/valider-kyc.sh $(Q)

dossier: ## Exerce le dossier livreur de bout en bout : depot, rejet, reprise, validation
# LE REJET EST LE COEUR DU SCENARIO, pas un ornement : la regle « apres un
# refus, on corrige et on resoumet » n'existe que dans l'agregat, et rien
# d'autre ne verifie qu'elle tient.
	./scripts/etape7-dossier-livreur.sh

clients: ## Exerce l'annuaire client : graduation par role, masque, cumul facture
# LA GRADUATION EST TOUT LE SUJET. Le script fabrique ses propres comptes ops
# et finance : tester avec le jeton d'admin ne prouverait rien, puisque c'est
# la difference entre les roles qu'on mesure.
	./scripts/clients-annuaire.sh

terrain: ## Une vraie course aux Quatre-Chemins (Aubervilliers), vers TON telephone
# IL NE FAIT PAS PASSER LE LIVREUR EN LIGNE, c'est justement ce qu'on teste :
# ouvre l'application, passe en ligne, puis lance cette cible. Le script mesure
# la distance au retrait et choisit le chemin — moteur si tu es dans les 6 km,
# offre manuelle sinon.
	./scripts/dispatch-terrain.sh

offre: ## Exerce l'offre manuelle : un livreur hors rayon recoit la course a la main
# CE QUI EST MESURE : le livreur est a DIX kilometres du retrait, donc hors des
# trois rayons du moteur. Le script verifie d'abord que le moteur ne lui
# propose rien, puis que l'exploitation y arrive. Si l'offre manuelle passait
# par le meme chemin que les vagues, l'etape 9 echouerait.
	./scripts/offre-manuelle.sh

versements: ## Exerce la chaine des gains : course livree, demande, approbation, virement
# CE QUI EST MESURE, ET QUI NE SE VOIT NULLE PART AILLEURS : le compte du
# livreur NE BOUGE PAS a l'approbation. Le script relit le releve entre
# l'approbation et la consignation du virement, et verifie que « deja verse »
# vaut encore zero. Si un jour quelqu'un debite a l'approbation pour « aller
# plus vite », c'est l'etape 12 qui tombera.
	./scripts/versements.sh

mesure-attente: ## Mesure ce que coute une heure d'attente a un livreur en ligne
# IL MESURE, IL N'ESTIME PAS, ET C'EST TOUTE LA DIFFERENCE. Le deuxieme audit
# de l'application livreur avance « environ 1,3 Mo par heure » — un ordre de
# grandeur pose a partir du nombre de requetes et d'une taille supposee. Un
# chiffre suppose ne doit pas servir a decider d'allonger un intervalle.
#
# LES INTERVALLES SONT LUS DANS home_screen.dart, jamais recopies : le jour ou
# le sondage change, la mesure suit toute seule.
	./scripts/mesure-attente.sh

fedapay: ## Les dernieres transactions telles que FedaPay les voit (make fedapay N=10)
# QUAND UN PAIEMENT ECHOUE, LA PAGE DU FOURNISSEUR NE DIT RIEN D'UTILE :
# « Transaction echouee. Veuillez reessayer ». Le statut, le mode et surtout le
# numero que FedaPay a retenu pour le client sont dans l'objet transaction.
# Deviner a partir du message de la page fait perdre une demi-journee.
#
# La cle vient du .env du service Paiement et n'est ni affichee ni passee en
# argument : un secret en argument finit dans l'historique du shell.
	./scripts/fedapay-transactions.sh $(if $(N),$(N),3) $(if $(BRUT),brut,)

webhook: ## Etat reel de la chaine webhook : tunnel, .env, conteneur, appels recus
# UN WEBHOOK QUI N'ARRIVE PAS NE PRODUIT AUCUNE ERREUR. Le client paie, la
# livraison reste en attente, et les journaux du service sont vides — puisque
# le service n'a rien recu. La preuve est en amont, chez ngrok, qui enregistre
# chaque requete, y compris celles qui finissent en 404.
	./scripts/webhook-etat.sh

fiche: ## Ou s'est arretee la creation d'une fiche client : make fiche Q=+22966000001
# LE 404 DE « GET /me » VIENT DE DIRECTORY, MAIS LA CAUSE EST EN AMONT. Trois
# maillons doivent tenir — Identity pose l'evenement, le publicateur l'envoie,
# Directory le consomme — et chacun peut lacher sans un mot. Ce script les
# regarde dans l'ordre et dit lequel a cede.
	./scripts/fiche-client.sh $(Q)

carte: ## Exerce la carte des positions : deux livreurs, fraicheur, refus a un livreur
# L'ETAPE 5 EST CELLE QUI COMPTE : une position vieillie doit sortir de la
# carte. Si la lecture partait de l'index GEO au lieu de l'ensemble des
# horodatages, tout le reste passerait et cette etape seule echouerait.
	./scripts/carte-positions.sh

etape7: ## Compare les chiffres de /kpi avec un SELECT direct dans chaque base
# UNE VERIFICATION QUI NE SAIT PAS QU'ELLE PASSE A VIDE NE VERIFIE RIEN : sur
# une base sans donnees, une requete fausse rend zero comme une juste. Le
# script sort en 2 dans ce cas plutot qu'en 0, et le dit.
	./scripts/etape7-verifier-kpi.sh

tunnel-stop: ## Ferme le tunnel ngrok
	@pkill -f "ngrok http" && echo "Tunnel ferme." || echo "Aucun tunnel ouvert."

migrations: ## Génère la migration initiale des services qui ont une base (make migrations NAME=Initial)
	./scripts/create-migrations.sh $(or $(NAME),Initial)

lint-proto: ## Lint des contrats protobuf
# « buf: command not found » NE DIT PAS OU LE PRENDRE. La cible echouait sur
# cette ligne seule, et un outil absent se confond alors avec un depot casse.
	@command -v buf >/dev/null 2>&1 || { \
	  echo "buf n'est pas installe."; \
	  echo "  brew install bufbuild/buf/buf     # ou : npm i -g @bufbuild/buf"; \
	  exit 1; \
	}
# 119 AVERTISSEMENTS PREEXISTENT, tous sur deux regles : le nom des messages de
# reponse, et leur partage entre plusieurs appels. Cette cible n'est donc jamais
# passee. Les corriger renommerait soixante-dix types du contrat, dans tous les
# services a la fois — une decision, pas un nettoyage. En attendant, elle sert a
# verifier qu'un fichier NEUF n'ajoute rien a la dette.
	cd contracts && buf lint

clean: ## Nettoie les artefacts de build
	dotnet clean HbaDelivery.sln
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

trace: ## Retrouve la pile derriere une reference d'erreur : make trace R=8143711a...
# « ERREUR INTERNE DU SERVICE. REFERENCE : X » NE DIT RIEN, PAR CONSTRUCTION.
# L'intercepteur gRPC refuse d'exposer une exception brute — elle contient des
# noms de tables, des hotes internes, parfois des valeurs — et rend une
# reference a la place. La pile, elle, est journalisee AVEC cette reference,
# dans le service qui a casse.
#
# ENCORE FAUT-IL SAVOIR LEQUEL. La reference voyage d'un service a l'autre par
# l'en-tete de correlation : on cherche donc dans tous, et celui qui repond est
# celui qui a lache. Chercher a la main service par service prend dix commandes
# et se termine souvent sur le mauvais.
	@test -n "$(R)" || { echo "Usage : make trace R=<reference>"; exit 1; }
	@for f in src/Services/*/docker-compose.yml src/Gateways/*/docker-compose.yml; do \
	  nom=$$(basename $$(dirname $$f)); \
	  sortie=$$(docker compose -f $$f logs --no-color --tail 5000 2>/dev/null \
	    | grep -A 40 -i "$(R)" || true); \
	  if [ -n "$$sortie" ]; then \
	    echo "═══ $$nom"; echo "$$sortie"; echo; \
	  fi; \
	done; \
	echo "(aucune autre occurrence)"

repartir: ## Vide TOUTES les donnees et repart a zero, l'admin recree au demarrage
# DESTRUCTIF ET SANS RETOUR. Le script demande une confirmation tapee, et
# affiche d'abord ce qu'il va detruire — une confirmation posee sans montrer ce
# qu'on perd se tape sans lire.
#
# L'ORDRE EST LA RAISON D'ETRE DU SCRIPT : arreter les services, supprimer les
# volumes, recreer les topics, reinitialiser le stockage objet, migrer, et
# seulement ensuite redemarrer — c'est le demarrage d'Identity qui recree
# l'administrateur. Une etape sautee laisse une pile qui repond a /health et ne
# fonctionne pas.
	./scripts/repartir-a-zero.sh

chaines: ## Ce que chaque conteneur a recu comme chaine de connexion (valeurs masquees)
# LE .ENV N'EST PAS CE QUI COMPTE, LE CONTENEUR SI. Un .env complet et un
# conteneur cree avant sa derniere modification donnent une chaine amputee, et
# la panne s'exprime tres loin : « No password has been provided », ou une base
# vide au milieu d'une trace EF Core.
#
# AUCUNE VALEUR N'EST AFFICHEE : la chaine porte le mot de passe de la base. On
# ne montre que les champs presents.
	./scripts/chaines-de-connexion.sh
