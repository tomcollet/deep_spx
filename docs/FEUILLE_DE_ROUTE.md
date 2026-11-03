# Feuille de route

| # | Étape | Livrable | Critère pour passer à la suite | État |
|---|---|---|---|---|
| 1 | Sonde (semaine 1) | `src/DeepGammaProbe` + verdicts des tests A, B et C | Les séries DeepGamma sont lisibles (Q1). Sinon : achat OPRA et calcul maison | **En cours** : sonde écrite et vérifiée sur imitation de l'API, à exécuter dans DeepCharts |
| 2 | Données (semaines 2-3) | Enregistreur DLL ; replay des 9 mois ; une ligne par minute + table d'événements | Minutes reconstruites depuis les `.tdatac` = barres DeepCharts | À faire |
| 3 | Faits connus (semaine 4) | Étude descriptive | Effets attendus retrouvés (vol réalisée vs GEX MM...) | À faire |
| 4 | Setups (semaines 5-8) | Une étude par setup (PREREG, résultats) | Critères de [PROTOCOLE.md](PROTOCOLE.md) | À faire |
| 5 | Simulation temps réel (4 semaines) | Moteur Python branché sur la DLL, ordres journalisés seulement | Slippage réel dans l'hypothèse du backtest ; mêmes signaux en live et en rejeu | À faire |
| 6 | Live | Un compte en MES, puis fan-out via le pupitre | Licence CME non-display réglée, règles de la prop firm vérifiées | À faire |

## Points à verrouiller avant le live

- **Licence CME** : l'usage de données par un système de trading automatisé ou semi-automatisé relève de la licence
  non-display, à clarifier avec CME.
- **Prop firms** : règles de chaque firme sur le trading automatisé et sur la copie entre firmes.
- **Données DeepGamma** : usage personnel uniquement ; aucune republication (sites, contenus).
