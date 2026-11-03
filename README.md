# deep_spx : ES intraday, order flow × positionnement gamma SPX

Algorithme intraday sur l'**ES**, horizon de quelques minutes à quelques heures. Il croise trois couches :

1. **le régime gamma** des market makers SPX (DeepGamma, données Cboe), qui dit quel type de trade est permis ;
2. **les zones** (murs gamma, Zero Gamma, VWAP, niveaux de séance), qui disent où agir ;
3. **l'order flow** de l'ES (agression, absorption, carnet), qui dit quand agir.

Les données arrivent par **DeepCharts desktop** : une DLL C# posée sur le graphique options SPX lit les trades, le
carnet (le flux L2) et les séries DeepGamma. Deepchart interdit à la DLL fichiers et réseau : elle écrit ses
enregistrements dans le log de Deepchart, que les outils relisent. L'exécution par le pupitre copy trade viendra en
dernier, une fois la stratégie validée.

## État

**Étape 1 sur 6 : la sonde (v2).** Elle vérifie que la DLL peut lire DeepGamma, avec quel historique, ce que donne le
carnet L2, et si tout fonctionne en Deep Replay et en live (issue #4). Elle respecte le bac à sable de Deepchart
(`tools/DllCheck` : aucune référence interdite), est vérifiée sur une imitation de l'API, et attend d'être lancée dans
DeepCharts.
Détail : [docs/FEUILLE_DE_ROUTE.md](docs/FEUILLE_DE_ROUTE.md).

## Démarrage (PC Windows avec DeepCharts)

```
git clone https://github.com/tomcollet/deep_spx
cd deep_spx
dotnet run --project tools\ApiDump                           # inventaire de l'API -> deepchart-api.txt
dotnet build src\DeepGammaProbe -p:DeepchartDevId=TON-ID      # sonde -> Documents\Deepchart\Indicators
dotnet run --project tools\DllCheck -- "%USERPROFILE%\Documents\Deepchart\Indicators\DeepSpx.Probe.dll"
python tools\extract_dspx.py CHEMIN_DU_LOG --out data\probe\testA   # après un test
```

Puis le protocole de test : [src/DeepGammaProbe/LISEZMOI.md](src/DeepGammaProbe/LISEZMOI.md).

Sans DeepCharts (Linux, CI) :

```
dotnet build tests/ProbeCompileCheck     # la sonde compile contre l'imitation de l'API
dotnet run --project tools/DllCheck -- tests/ProbeCompileCheck/bin/Debug/net10.0/ProbeCompileCheck.dll
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
| `tools/DllCheck` | Imite le contrôle de Deepchart : signale ce que la DLL référence d'interdit (repris de `tomcollet/deep`) |
| `tools/extract_dspx.py` | Relit le log de Deepchart et range les lignes `DSPX;` de la DLL en fichiers CSV |
| `docs/` | Stratégie, architecture, protocole de recherche, feuille de route, ce qu'on sait du SDK |
| `research/` | Labo Python (à venir) |
| `engine/` | Moteur de décision temps réel (à venir) |

## Documents

- [Stratégie](docs/STRATEGIE.md) : hypothèse, trois couches, trois setups, filtres.
- [Architecture](docs/ARCHITECTURE.md) : le bac à sable de Deepchart, le log comme canal de sortie, moteur C# pur.
- [Protocole](docs/PROTOCOLE.md) : données, holdout, coûts, critères d'acceptation.
- [Feuille de route](docs/FEUILLE_DE_ROUTE.md) : étapes et critères pour passer à la suivante.
- [SDK DeepCharts](docs/SDK_DEEPCHARTS.md) : bac à sable, données, carnet L2, DeepGamma, issues connues.

> Aucun algorithme ne garantit des gains. Rien ne passe en argent réel sans avoir tenu sur le holdout puis en
> simulation temps réel.
