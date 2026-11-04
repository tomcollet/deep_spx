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
  explorer. **Exception décidée par Tom le 04/11/2026 : ouvert pour l'étude des scalps seulement** (60 jours chargés
  sur le graphique, jusqu'au 04/11 inclus) ; le test final des scalps = séances live à partir du 05/11/2026, jamais dans
  l'étude (`StudyEndDate` = 20261105). A/B/C gardent le holdout du 16/08.
- **Règle de risque de Tom** (25 ticks de risque au plus, objectif d'au moins 1,5R), posée le 04/11/2026 puis **retirée
  par lui le même jour à 20h37** : désactivée par défaut (`MaxRiskTicks` = 0, `MinRewardRisk` = 0), toujours
  disponible en paramètre. Les scalps (étude close) l'avaient appliquée.
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
- **Lecture des résultats** : `tools/read_log_window.py --follow --out data\log.txt` sur le PC de Tom (fenêtre Log
  ouverte et remontée en haut) ; Tom envoie le fichier. Plus de captures d'écran sauf si le script ne lit rien.
- **Heures** : `exchDt` est à l'heure de New York sur le graphique de Tom (constaté le 04/11/2026). Le moteur est réglé
  à New York par défaut ; l'indicateur détecte l'horloge des barres (`DSPX;CLOCK`). Ne jamais supposer Chicago.
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

1. Fait le 04/11 : indicateur de setups posé, horloge New York détectée, repérage complet (envelopes 10, summary 12,
   netflow 13, pressure 14), Zero Gamma du panneau = ligne tracée (niveaux en prix ES). Tom passe en v1.5 (ticks).
2. Q7 (04/11, `docs/SDK_DEEPCHARTS.md`) : Envelopes et Net Flow lisibles en live ; Summary et MM Pressure remplis avec
   3 minutes de retard, mesuré par les lignes `DSPX;LAG` (lus avec `SummaryLagBars` = 5).
3. Test C (Deep Replay), lignes capturées par `tools/read_log_window.py --follow`.
4. Étude des scalps faite le 04/11 (41 séances) : **échec d'H1 et d'H2**, piste close
   (`research/scalp_flux/RESULTATS.md`). Ne pas retester de variante sur ces 41 séances. Reste ouvert : le carnet
   (enregistrement live à faire).
5. Recherche de configuration A/B/C (`research/optim_abc/`) : DeepGamma seulement depuis le 11/05/2026, donc 44
   séances avant le holdout. Passage 1 (64 configurations, règle de risque) : rien de retenu. Passage 2 (v1.8,
   104 configurations, sans règle de risque, classement par taux de gain parmi les configurations gagnantes) : à
   lancer. Validation moins indépendante (mêmes séances) : le juge sera le holdout d'A/B/C, ouvert une fois sur
   décision de Tom pour la configuration retenue.
6. Ensuite : enregistrement du carnet en live, backtest C# en ligne de commande réutilisant `Engine/`.
