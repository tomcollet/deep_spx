# deep_spx : ES intraday, order flow × positionnement gamma SPX

Algorithme intraday sur l'**ES**, horizon de quelques minutes à quelques heures. Il croise trois couches :

1. **le régime gamma** des market makers SPX (DeepGamma, données Cboe), qui dit quel type de trade est permis ;
2. **les zones** (murs gamma, Zero Gamma, VWAP, niveaux de séance), qui disent où agir ;
3. **l'order flow** de l'ES (agression, absorption, carnet), qui dit quand agir.

Les données arrivent par **DeepCharts desktop** : une DLL C# posée sur le graphique options SPX lit les trades, le
carnet et les séries DeepGamma. Un moteur Python décide. L'exécution par le pupitre copy trade viendra en dernier, une
fois la stratégie validée.

## État

**Étape 1 sur 6 : la sonde.** Elle vérifie que la DLL peut lire DeepGamma, avec quel historique, et si tout fonctionne
en Deep Replay. Elle est écrite et vérifiée sur une imitation de l'API, et attend d'être lancée dans DeepCharts.
Détail : [docs/FEUILLE_DE_ROUTE.md](docs/FEUILLE_DE_ROUTE.md).

## Démarrage (PC Windows avec DeepCharts)

```
git clone https://github.com/tomcollet/deep_spx
cd deep_spx
dotnet run --project tools\ApiDump                           # inventaire de l'API -> deepchart-api.txt
dotnet build src\DeepGammaProbe -p:DeepchartDevId=TON-ID      # sonde -> Documents\Deepchart\Indicators
```

Puis le protocole de test : [src/DeepGammaProbe/LISEZMOI.md](src/DeepGammaProbe/LISEZMOI.md).

Sans DeepCharts (Linux, CI) :

```
dotnet build tests/ProbeCompileCheck     # la sonde compile contre l'imitation de l'API
dotnet run --project tests/ProbeHarness  # graphique simulé : les verdicts doivent tous finir en OK
```

## Organisation

| Dossier | Contenu |
|---|---|
| `src/DeepGammaProbe` | Sonde : indicateur DeepCharts en lecture seule, et son protocole de test |
| `tests/DeepchartApiStubs` | Imitation de l'API DeepCharts, reprise de `tomcollet/deep`, pour compiler sans Deepchart |
| `tests/ProbeCompileCheck` | Vérifie que la sonde compile (avertissements traités comme des erreurs) |
| `tests/ProbeHarness` | Graphique simulé qui vérifie les verdicts de la sonde |
| `tools/ApiDump` | Liste l'API réelle des DLL de Deepchart sans les exécuter (repris de `tomcollet/deep`) |
| `docs/` | Stratégie, architecture, protocole de recherche, feuille de route, ce qu'on sait du SDK |
| `research/` | Labo Python (à venir) |
| `engine/` | Moteur de décision temps réel (à venir) |

## Documents

- [Stratégie](docs/STRATEGIE.md) : hypothèse, trois couches, trois setups, filtres.
- [Architecture](docs/ARCHITECTURE.md) : DLL capteur, moteur, même chaîne en replay et en live.
- [Protocole](docs/PROTOCOLE.md) : données, holdout, coûts, critères d'acceptation.
- [Feuille de route](docs/FEUILLE_DE_ROUTE.md) : étapes et critères pour passer à la suivante.
- [SDK DeepCharts](docs/SDK_DEEPCHARTS.md) : ce qui est confirmé, ce que la sonde doit lever.

> Aucun algorithme ne garantit des gains. Rien ne passe en argent réel sans avoir tenu sur le holdout puis en
> simulation temps réel.
