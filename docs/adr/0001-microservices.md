# 0001 — Microservices plutôt que monolithe modulaire

Statut : acceptée — septembre 2026

## Contexte

HBA Delivery sert trois consommateurs aux rythmes très différents : HBA Express,
HBA Food, et à terme des partenaires externes. Les charges ne se ressemblent
pas : Dispatch et Driver encaissent un flux de positions continu, Delivery a un
volume d'écritures modeste, Notification part en pics.

Un monolithe modulaire aurait coûté moins cher à démarrer.

## Décision

Sept services séparés au MVP : Identity, Delivery, Pricing, Dispatch, Driver,
Payment, Notification. Une base PostgreSQL par service, sans accès croisé, même
en lecture.

## Conséquences

- Dispatch et Driver peuvent monter en charge sans entraîner le reste.
- La panne d'un service ne fige pas les autres, à condition que les appels
  synchrones portent une échéance — d'où l'intercepteur de deadline.
- Le prix à payer est réel : pas de transaction commune, pas de jointure entre
  services, et un développement local qui demande Docker. C'est la raison d'être
  de l'Outbox (ADR 0003).
- Tant qu'un service n'a pas de modèle propre, sa coquille ne coûte presque
  rien ; c'est le cas de six des sept aujourd'hui.
