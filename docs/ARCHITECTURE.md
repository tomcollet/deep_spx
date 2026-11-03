# Architecture

```
Flux de marché ─► DeepCharts desktop (PC Windows)
                   └─ graphique options SPX (symbole ES-CME, 1 min)
                       ├─ indicateurs DeepGamma ─┐ séries lues par GetSeriesValueById
                       └─ DLL deep_spx (C#) ◄────┘ + trades (OnTick) + carnet (OnBook)
                              │ features calculées en mémoire
                              ▼ socket local 127.0.0.1
                          Moteur Python (PC Windows)
                              │ règles, filtres, risque, journal
                              ▼ (dernière étape seulement)
                          Pupitre copy trade, via tunnel SSH vers vps-us en 127.0.0.1
```

## Une seule DLL, la nôtre

DeepCharts est l'application hôte. DeepGamma est un ensemble d'indicateurs de DeepCharts, posés sur le même graphique ;
on ne touche pas à leur code. Notre DLL ne lit pas DeepGamma directement : elle demande au graphique la valeur de la
série X de l'indicateur Y. VolSysAPI.dll et VolumetricaCore.dll sont les références de compilation (l'API), pas des
données.

## Rôles

- **DLL (C#) = capteur.** Elle reçoit chaque trade (prix, taille, côté agresseur, bid/ask, heure de bourse), chaque
  changement du carnet, et lit les séries DeepGamma à chaque nouvelle minute. Elle calcule les features (delta par
  minute, OFI 30 s, profondeur, état d'absorption) et les publie. **Les features ne sont codées qu'une fois, en C#.**
- **Moteur (Python) = décision.** Il croise régime, zone et déclencheur, applique filtres et risque, émet BUY / SELL /
  FLAT et journalise tout. **Les règles ne sont codées qu'une fois, en Python**, et ce sont les mêmes en recherche et en
  live.

## Même chaîne en backtest, en simulation et en live

En Deep Replay, la DLL produit exactement les mêmes messages qu'en live, et le moteur les traite de la même façon. Le
replay sert donc à la fois de générateur d'historique (features + DeepGamma) et de backtest de toute la chaîne.

## Fonctionnement continu

- **Démarrage** : au chargement, la DLL calcule l'historique du graphique pour remplir les percentiles, et n'émet des
  signaux qu'une fois en temps réel.
- **Garde-fou** : signal de vie chaque seconde. Le moteur cesse de prendre des trades s'il n'en reçoit plus, si aucun
  trade n'arrive pendant la séance, ou si DeepGamma n'a pas bougé depuis 3 minutes.
- **Journal** : chaque message est horodaté ; une journée se rejoue à l'identique et se compare au backtest.
- **Poste** : DeepCharts ouvert, mise en veille et redémarrages Windows Update coupés pendant la séance. À terme, un VPS
  Windows.

## Le pupitre : en dernier

Le pupitre n'est branché qu'une fois la stratégie validée. Jusque-là, le moteur journalise ses ordres et simule leur
exécution (entrée au marché au bid ou à l'ask du tick suivant, + 1 tick). Le pupitre a ses propres règles non
négociables (dépôt `copytrade`) : BUY/SELL en tout-ou-rien, FLAT jamais bloqué, rien n'écoute ailleurs que sur
127.0.0.1. L'intégration les respectera.
