# Moteur (Python)

Le moteur de décision temps réel. Rien n'y est encore codé : il démarre à l'étape 5 de la
[feuille de route](../docs/FEUILLE_DE_ROUTE.md), avec les règles validées par la recherche.

## Rôle

- Écouter la DLL sur un socket local (127.0.0.1) : features, séries DeepGamma, signal de vie.
- Appliquer les règles **du même code** que la recherche : régime, zone, déclencheur, filtres, risque.
- Journaliser chaque message et chaque décision avec l'heure, pour rejouer une journée à l'identique.
- Garde-fou : plus de nouveaux trades si la DLL se tait, si aucun trade n'arrive en séance, ou si DeepGamma ne bouge
  plus depuis 3 minutes.

## Exécution

Jusqu'à la validation, **aucun ordre n'est envoyé** : exécution simulée (bid/ask du tick suivant + 1 tick). Le pupitre
copy trade est la toute dernière étape. Voir [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).
