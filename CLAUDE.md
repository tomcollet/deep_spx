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
- **Même chaîne en Deep Replay, en simulation et en live.**
- **Bac à sable de Deepchart** (doc troubleshooting) : la DLL n'a droit ni aux fichiers, ni au réseau, ni à la
  réflexion, ni aux threads ni à `lock`. Sortie prévue : le log de Deepchart (`VAn.AddToLog`, lignes `DSPX;`), **mais ce
  log est chiffré sur disque (constaté le 04/11/2026)** : ne pas chercher à le déchiffrer. La sonde v4 affiche ses
  résultats sur le graphique ; le canal de sortie du moteur est à redéfinir (voir `docs/ARCHITECTURE.md`).
- **Moteur C# pur dans la DLL** : la décision initiale « règles en Python reliées par socket » est impossible (pas de
  socket). Le 04/11/2026, Tom a demandé de continuer l'indicateur de setups : le moteur vit dans
  `src/DeepSpxSetups/Engine` (C# pur, sans Deepchart), compilé dans la DLL et dans `tests/EngineChecks` ; un backtest
  en ligne de commande le réutilisera. Python pour explorer seulement. Voir `docs/SETUPS.md`.
- **Indicateur de setups v1** : signaux seulement, aucun ordre. Seuils = valeurs de départ de `docs/STRATEGIE.md`, non
  réglées ; ne pas les régler sur les séances du holdout. Statistiques masquées par défaut (`ShowStats`).
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
- **Stubs** (`tests/DeepchartApiStubs`) : imitation de l'API pour compiler sans Deepchart, alignée sur
  `deepchart-api.txt` du 04/11/2026 (VolSysAPI 1.4.6.0). Ne les modifier que pour refléter l'API réelle, jamais pour faire
  passer du code. Points piégeux : `GetSeriesValueById` renvoie `double[]`, `VolTotList` est un tableau,
  `CallHandler` vaut Off = 0, HistRT = 1, RT = 2.
- **Avant chaque commit touchant une DLL** :
  - sonde : `dotnet build tests/ProbeCompileCheck`, puis
    `dotnet run --project tools/DllCheck -- tests/ProbeCompileCheck/bin/Debug/net10.0/ProbeCompileCheck.dll` (aucune
    référence interdite), puis `dotnet run --project tests/ProbeHarness` (tout doit finir en OK) ;
  - setups : `dotnet run --project tests/EngineChecks`, `dotnet build tests/SetupsCompileCheck`, puis
    `dotnet run --project tools/DllCheck -- tests/SetupsCompileCheck/bin/Debug/net10.0/SetupsCompileCheck.dll`, puis
    `dotnet run --project tests/SetupsHarness` (tout en OK).
- **Rien dans `Engine/` ne dépend de Deepchart** (ni `VolSysAPI`, ni `VolumetricaCore`) et tout y respecte aussi le bac
  à sable, puisque le même code est compilé dans la DLL.
- **Dans la DLL** : pas d'indexeur `this[...]`, pas de `params`, pas de `ex.GetType().Name`, pas de `System.IO`,
  `System.Net`, `System.Threading`, `System.Reflection`, `Environment`. Les signatures inconnues se lisent avec
  `tools/ApiDump`, jamais par réflexion dans la DLL.
- **Toute étude** commence par un `PREREG.md` dans `research/<etude>/`.
- Vérifier avant d'affirmer : le SDK est peu documenté, et ce qui n'a pas été vu dans du code ou une sortie de la sonde
  reste une hypothèse.

## Dépôts liés

- `tomcollet/deep` : OF Scalper (NQ). Origine des stubs et d'ApiDump ; `docs/RESULTATS.md` contient les leçons
  (order flow seul sans edge au contact des niveaux, cassure de la veille sans edge sur ES 2025, séries exposées par
  Deep Stats).
- Pupitre copy trade : dépôt `copytrade` sur le Mac de Tom, VPS `vps-us`.

## Prochaine action

1. Tom compile et pose l'indicateur de setups ; vérifier que le Zero Gamma du panneau tombe sur la ligne DeepGamma
   (sinon `ApplyOptionOffset`, ou les niveaux sont en prix SPX).
2. Test B de la sonde en séance US (Q7, issue #4) : si DeepGamma est figé ou nul en live, l'indicateur reste en régime
   inconnu, ce qui est voulu ; la recherche continue en Deep Replay.
3. Test C (Deep Replay), lignes capturées par `tools/read_log_window.py --follow`.
4. Ensuite : enregistreur par minute (étape 2) et backtest C# en ligne de commande réutilisant `Engine/`, puis une étude
   préenregistrée par setup sur les séances d'avant le 16/08/2026.
