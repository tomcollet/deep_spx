# Protocole de recherche

## Données

| Source | Contenu | Rôle |
|---|---|---|
| `.tdatac` DeepCharts (contrats 202603 à 202612, sur le Mac dans `~/Claude/Projects/Ticks`) | Trades tick par tick avec bid/ask (flux zstd d'enregistrements de 65 octets) | Référence pour les trades, les features d'agression et la simulation d'exécution |
| Enregistreur DLL en Deep Replay | Séries DeepGamma + features ES + carnet, minute par minute | Régime et zones ; carnet si le replay le rejoue |
| Exports minutes DeepCharts (1 ou 2 séances) | Barres 1 min | Contrôle de l'agrégation et des horodatages, rien d'autre |

- **Ticks plutôt que minutes** : les déclencheurs se calculent trade par trade, et seuls les ticks disent si le stop ou
  l'objectif a été touché en premier dans une minute.
- **Jointure** sur des minutes en UTC. DeepGamma se met à jour chaque minute.
- **Carnet** : les `.tdatac` n'en ont pas. Si le replay ne le rejoue pas, historique MBO via Databento (l'historique CME
  ne demande pas de licence).
- Les données ne sont **jamais committées** (`data/` est ignoré).

## Découpage

- **Holdout scellé : toutes les séances à partir du 16/08/2026**, la même coupure que le banc `Ticks/lab`. Il ne
  s'ouvre qu'une fois, pour la décision finale.
- **Travail** : du début de la fenêtre de replay DeepGamma (environ février 2026) au 15/08/2026, soit environ
  130 séances, en walk-forward.
- Les séances enregistrées en live à partir de maintenant s'ajoutent comme hors échantillon supplémentaire.

## Coûts

- Entrée et sortie **au marché** (le pupitre clique) : exécution au bid ou à l'ask du tick qui suit le signal, + 1 tick
  de slippage. Robustesse testée à 2 ticks.
- ES : 1 tick = 12,50 $ par contrat, frais en plus.

## Étapes d'une étude

1. **PREREG.md** dans `research/<etude>/` avant tout test : hypothèse, paramètres et grille, critères, découpage.
2. **Faits connus** d'abord. Par exemple, une vol réalisée plus faible quand le GEX MM est positif. Si on ne les
   retrouve pas, la lecture des données est fausse.
3. **Un setup à la fois.**
4. **Registre de tous les essais**, pour tenir compte du nombre de tentatives (deflated Sharpe).

## Critères d'acceptation d'un setup

- Espérance nette d'au moins **1 tick par trade** après coûts, sur les plis hors échantillon du walk-forward **et** sur
  le holdout.
- Positif sur chaque moitié de la période de travail.
- Nombre de trades suffisant pour que l'intervalle de confiance exclue zéro. Ce seuil sera fixé dans le PREREG, une
  fois le volume réel de données connu.
- Robuste à 2 ticks de slippage.

Puis simulation en temps réel (4 semaines, aucun ordre) : le slippage observé doit rester dans l'hypothèse du backtest.
