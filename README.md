# deep_spx : ES intraday, order flow × positionnement gamma SPX

Algorithme intraday sur l'**ES**, horizon de quelques minutes à quelques heures. Il croise trois couches :

1. **le régime gamma** des market makers SPX (DeepGamma, données Cboe), qui dit quel type de trade est permis ;
2. **les zones** (murs gamma, Zero Gamma, VWAP, niveaux de séance), qui disent où agir ;
3. **l'order flow** de l'ES (agression, absorption, carnet), qui dit quand agir.

Les données arrivent par **DeepCharts desktop** : une DLL C# posée sur le graphique options SPX lit les trades, le
carnet (le flux L2) et les séries DeepGamma. Deepchart interdit à la DLL fichiers et réseau : elle affiche ses
résultats sur le graphique et écrit ses lignes `DSPX;` dans la fenêtre Log de Deepchart, que
`tools/read_log_window.py` relit (le fichier de log est chiffré). L'exécution par le pupitre copy trade viendra en
dernier, une fois la stratégie validée.

## État

**Étape 1 sur 6 : la sonde (v4.1)**, lancée dans DeepCharts le 04/11/2026 : DeepGamma est lisible avec son historique
(Q1, Q2), le carnet est complet, chaque trade porte les identifiants d'ordres. Reste le test en séance US (Q7,
issue #4) et le test en Deep Replay.

**En parallèle : l'indicateur de setups (v1)** `DeepSpx - Setups gamma x order flow`. Il signale sur le graphique les
setups A, B et C de la stratégie, simule leur exécution et journalise chaque signal. **Il n'envoie aucun ordre et ses
seuils ne sont pas validés.** Détail : [docs/SETUPS.md](docs/SETUPS.md).

Feuille de route : [docs/FEUILLE_DE_ROUTE.md](docs/FEUILLE_DE_ROUTE.md).

## Démarrage (PC Windows avec DeepCharts)

```
git clone https://github.com/tomcollet/deep_spx
cd deep_spx
dotnet run --project tools\ApiDump                           # inventaire de l'API -> deepchart-api.txt
dotnet build src\DeepGammaProbe -p:DeepchartDevId=TON-ID      # sonde -> Documents\Deepchart\Indicators
dotnet run --project tools\DllCheck -- "$env:USERPROFILE\Documents\Deepchart\Indicators\DeepSpx.Probe.dll"
dotnet build src\DeepSpxSetups -p:DeepchartDevId=TON-ID      # setups -> Documents\Deepchart\Indicators
dotnet run --project tools\DllCheck -- "$env:USERPROFILE\Documents\Deepchart\Indicators\DeepSpx.Setups.dll"
python tools\read_log_window.py --follow --out data\setups\signaux.txt   # lignes DSPX; de la fenêtre Log
python tools\extract_dspx.py CHEMIN_DU_LOG --out data\probe\testA   # après un test
```

Puis le protocole de test : [src/DeepGammaProbe/LISEZMOI.md](src/DeepGammaProbe/LISEZMOI.md).

Sans DeepCharts (Linux, CI) :

```
dotnet build tests/ProbeCompileCheck     # la sonde compile contre l'imitation de l'API
dotnet run --project tools/DllCheck -- tests/ProbeCompileCheck/bin/Debug/net10.0/ProbeCompileCheck.dll
dotnet run --project tests/ProbeHarness  # graphique simulé : les verdicts doivent tous finir en OK
dotnet run --project tests/EngineChecks  # scénarios du moteur de setups
dotnet build tests/SetupsCompileCheck    # l'indicateur de setups compile contre l'imitation de l'API
dotnet run --project tools/DllCheck -- tests/SetupsCompileCheck/bin/Debug/net10.0/SetupsCompileCheck.dll
dotnet run --project tests/SetupsHarness # graphique simulé : repérage DeepGamma, signaux, journal, panneau
```

## Organisation

| Dossier | Contenu |
|---|---|
| `src/DeepGammaProbe` | Sonde : indicateur DeepCharts en lecture seule, et son protocole de test |
| `src/DeepSpxSetups` | Indicateur de setups ; son moteur C# pur dans `Engine/` (régime, zones, setups, exécution simulée) |
| `tests/DeepchartApiStubs` | Imitation de l'API DeepCharts, reprise de `tomcollet/deep`, pour compiler sans Deepchart |
| `tests/ProbeCompileCheck` | Vérifie que la sonde compile (avertissements traités comme des erreurs) |
| `tests/ProbeHarness` | Graphique simulé qui vérifie les verdicts de la sonde |
| `tests/EngineChecks` | Scénarios synthétiques du moteur de setups, hors de Deepchart |
| `tests/SetupsCompileCheck`, `tests/SetupsHarness` | Compilation et graphique simulé de l'indicateur de setups |
| `tools/ApiDump` | Liste l'API réelle des DLL de Deepchart sans les exécuter (repris de `tomcollet/deep`) |
| `tools/DllCheck` | Imite le contrôle de Deepchart : signale ce que la DLL référence d'interdit (repris de `tomcollet/deep`) |
| `tools/read_log_window.py` | Lit les lignes `DSPX;` affichées dans la fenêtre Log de DeepCharts (UI Automation de Windows) |
| `tools/extract_dspx.py` | Range les lignes `DSPX;` lues en fichiers CSV par type |
| `docs/` | Stratégie, architecture, protocole de recherche, feuille de route, ce qu'on sait du SDK |
| `research/` | Labo Python (à venir) |

## Documents

- [Stratégie](docs/STRATEGIE.md) : hypothèse, trois couches, trois setups, filtres.
- [Setups](docs/SETUPS.md) : ce que l'indicateur v1 implémente, avec les substitutions et les paramètres.
- [Architecture](docs/ARCHITECTURE.md) : le bac à sable de Deepchart, le log comme canal de sortie, moteur C# pur.
- [Protocole](docs/PROTOCOLE.md) : données, holdout, coûts, critères d'acceptation.
- [Feuille de route](docs/FEUILLE_DE_ROUTE.md) : étapes et critères pour passer à la suivante.
- [SDK DeepCharts](docs/SDK_DEEPCHARTS.md) : bac à sable, données, carnet L2, DeepGamma, issues connues.

> Aucun algorithme ne garantit des gains. Rien ne passe en argent réel sans avoir tenu sur le holdout puis en
> simulation temps réel.
