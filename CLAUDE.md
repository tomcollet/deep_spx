# CLAUDE.md : reprise du projet deep_spx

Projet de Tom : algorithme intraday ES qui croise le positionnement gamma SPX (DeepGamma) et l'order flow ES. Langue de
travail : **français** (docs, commentaires, messages de commit).

## Lire d'abord

1. `README.md` : objectif et état.
2. `docs/FEUILLE_DE_ROUTE.md` : étape en cours et critère pour passer à la suivante.
3. `docs/STRATEGIE.md`, `docs/PROTOCOLE.md`, `docs/ARCHITECTURE.md`, `docs/SDK_DEEPCHARTS.md`.

## Décisions prises (ne pas revenir dessus sans que Tom le demande)

- **ES**, horizon minutes à heures, intraday. MES pour le dimensionnement.
- **Données via DeepCharts desktop** sur le PC Windows de Tom : une seule DLL (la nôtre), posée sur le graphique options
  SPX (symbole ES-CME), qui lit trades, carnet et séries DeepGamma. Pas d'achat OPRA/Cboe tant que la sonde n'a pas
  montré que DeepGamma est illisible.
- **Features codées une fois en C# (DLL), règles une fois en Python (moteur)** ; même chaîne en Deep Replay, en
  simulation et en live.
- **Ticks `.tdatac` comme référence** des trades ; minutes reconstruites depuis les ticks ; exports minutes DeepCharts
  uniquement pour le contrôle.
- **Holdout scellé : séances à partir du 16/08/2026** (même coupure que le banc Ticks/lab). Ne jamais l'ouvrir pour
  explorer.
- **Exécution simulée** (bid/ask du tick suivant + 1 tick, robustesse à 2 ticks) jusqu'à la validation.
- **Le pupitre copy trade est la dernière étape.** Ne pas l'intégrer avant que Tom le demande. Ses règles : BUY/SELL en
  tout-ou-rien, FLAT jamais bloqué, rien n'écoute ailleurs que sur 127.0.0.1.

## Conventions

- **Developer id DeepCharts** : jamais committé. Toujours `-p:DeepchartDevId=...` en ligne de commande.
- **Rien de propriétaire dans le dépôt** : ni données de marché, ni sorties de la sonde, ni `deepchart-api.txt`
  (`.gitignore`).
- **Stubs** (`tests/DeepchartApiStubs`) : imitation de l'API pour compiler sans Deepchart. Ne les modifier que pour
  refléter l'API réelle (vue dans `deepchart-api.txt` ou `api_dump.txt`), jamais pour faire passer du code.
- **Avant chaque commit touchant la DLL** :
  `dotnet build tests/ProbeCompileCheck` puis `dotnet run --project tests/ProbeHarness` (tout doit finir en OK).
- **Toute étude** commence par un `PREREG.md` dans `research/<etude>/`.
- Vérifier avant d'affirmer : le SDK est peu documenté, et ce qui n'a pas été vu dans du code ou une sortie de la sonde
  reste une hypothèse.

## Dépôts liés

- `tomcollet/deep` : OF Scalper (NQ). Origine des stubs et d'ApiDump ; `docs/RESULTATS.md` contient les leçons
  (order flow seul sans edge au contact des niveaux, cassure de la veille sans edge sur ES 2025, séries exposées par
  Deep Stats).
- Pupitre copy trade : dépôt `copytrade` sur le Mac de Tom, VPS `vps-us`.

## Prochaine action

Attendre les sorties de la sonde (tests A, B, C de `src/DeepGammaProbe/LISEZMOI.md`) et `deepchart-api.txt` :
- Q1 OUI → écrire l'enregistreur (étape 2) ;
- Q1 NON → étudier `deepchart-api.txt` et les identifiants du Pattern Builder ; en dernier recours, plan B OPRA.
