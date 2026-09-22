# Raccourcis du quotidien. `make` seul affiche l'aide.

.DEFAULT_GOAL := help
.PHONY: help up down logs build test test-domain test-arch test-e2e migrations lint-proto clean

help: ## Affiche cette aide
	@grep -E '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

up: ## Démarre les dépendances locales
	docker compose -f deploy/docker-compose.yml up -d

down: ## Arrête les dépendances locales
	docker compose -f deploy/docker-compose.yml down

logs: ## Suit les journaux des dépendances
	docker compose -f deploy/docker-compose.yml logs -f

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

migrations: ## Génère la migration initiale des quatre services (make migrations NAME=Initial)
	./scripts/create-migrations.sh $(or $(NAME),Initial)

lint-proto: ## Lint des contrats protobuf
	cd contracts && buf lint

clean: ## Nettoie les artefacts de build
	dotnet clean HbaDelivery.sln
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
