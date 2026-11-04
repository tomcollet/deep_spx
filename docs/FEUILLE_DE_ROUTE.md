# Feuille de route

| # | Étape | Livrable | Critère pour passer à la suite | État |
|---|---|---|---|---|
| 1 | Sonde (semaine 1) | `src/DeepGammaProbe` (v4.1, panneau sur le graphique) + `deepchart-api.txt` + verdicts des tests A, B et C | Les séries DeepGamma sont lisibles en replay (Q1, Q2). Sinon : achat OPRA et calcul maison | **En cours** : tests A et B faits le 04/11/2026 (Q1 à Q4, Q6 OUI ; Q7 : Envelopes et Net Flow à jour en live, Summary et Pressure avec 3 minutes de retard ; Q8 : pas de MBO, identifiants d'ordres sur chaque trade). Reste le test C en Deep Replay |
| 2 | Données (semaines 2-3) | `Engine/` C# pur + enregistreur dans la DLL ; replay des 9 mois ; une ligne par minute dans le log | Minutes reconstruites depuis les `.tdatac` = barres DeepCharts | À faire |
| 3 | Faits connus (semaine 4) | Étude descriptive | Effets attendus retrouvés (vol réalisée vs GEX MM...) | À faire |
| 4 | Setups (semaines 5-8) | Une étude par setup (PREREG, résultats) | Critères de [PROTOCOLE.md](PROTOCOLE.md) | **En cours** : indicateur de setups ([SETUPS.md](SETUPS.md)) ; recherche de configuration A/B/C codée (v1.7, [PREREG](../research/optim_abc/PREREG.md)), à lancer sur 1 an de minutes |
| 4 bis | Scalps (demande de Tom, 04/11/2026) | Étude H1 / H2 dans l'indicateur ([PREREG](../research/scalp_flux/PREREG.md)), 60 jours chargés | Critères du PREREG en découverte et validation, puis test final sur les séances live à partir du 05/11/2026 | **Échec le 04/11/2026** (41 séances) : H1 et H2 font comme le hasard, environ −2 ticks par trade, le coût d'exécution. Piste close ([résultats](../research/scalp_flux/RESULTATS.md)) |
| 5 | Simulation temps réel (4 semaines) | Moteur dans la DLL, signaux dans le log, ordres simulés | DeepGamma non nul en live (issue #4 corrigée) ; slippage réel dans l'hypothèse du backtest ; mêmes signaux en live et en rejeu | À faire |
| 6 | Live | Un compte en MES, puis fan-out via le pupitre | Licence CME non-display réglée, règles de la prop firm vérifiées | À faire |

## Risques identifiés

- **Indicateur de setups avant les études** : il est tentant de régler ses seuils en regardant les signaux des séances
  récentes, qui sont dans le holdout (à partir du 16/08/2026). Les statistiques sont donc masquées par défaut
  (`ShowStats`) ; tout réglage passe par une étude préenregistrée sur les séances d'avant.

- **Issue #4 du dépôt officiel** : DeepGamma lu par `GetSeriesValueById` vaut 0 en live (juste en replay). Bloque l'étape
  5, pas la recherche. À suivre, et à signaler à Volumetrica avec nos propres mesures (test B de la sonde).
- **Carnet en replay** : s'il n'est pas rejoué, les features de liquidité n'auront d'historique qu'à partir du début de
  notre enregistrement, ou via un historique MBO acheté.
- **Bac à sable** : aucune sortie de la DLL hors du log de Deepchart. Latence et rotation du log à mesurer avant le live.

## Points à verrouiller avant le live

- **Licence CME** : l'usage de données par un système de trading automatisé ou semi-automatisé relève de la licence
  non-display, à clarifier avec CME.
- **Prop firms** : règles de chaque firme sur le trading automatisé et sur la copie entre firmes.
- **Données DeepGamma** : usage personnel uniquement ; aucune republication (sites, contenus).
