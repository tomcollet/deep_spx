# Moteur

Rien n'y est encore codé : le moteur démarre à l'étape 2 de la [feuille de route](../docs/FEUILLE_DE_ROUTE.md).

## Proposition (à valider par Tom)

Deepchart interdit à la DLL fichiers et réseau : un moteur Python relié à la DLL par socket est impossible. La
proposition est un **moteur C# pur**, sans dépendance à Deepchart, sur le modèle de `tomcollet/deep`
(`src/OrderFlowScalper/Engine`) :

- compilé **dans la DLL**, qui l'alimente avec les trades, le carnet et les séries DeepGamma ;
- compilé **dans un outil de backtest** en ligne de commande, qui rejoue les `.tdatac` et les enregistrements du log ;
- testé par des tests unitaires ordinaires, hors de Deepchart.

Il contiendra les features, le régime, les zones, les déclencheurs, les filtres et le risque, en un seul exemplaire.
Python sert à explorer (dossier `research/`). Détail : [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).

## Exécution

Jusqu'à la validation, **aucun ordre n'est envoyé** : exécution simulée (bid ou ask du tick suivant + 1 tick). Les
signaux sortent par le log de Deepchart ; le pupitre copy trade, branché sur ce log, est la toute dernière étape.
