# Architecture

## La contrainte qui décide de tout

Deepchart refuse une DLL qui écrit des fichiers, ouvre une connexion réseau, crée des threads ou utilise la réflexion
(détail : [SDK_DEEPCHARTS.md](SDK_DEEPCHARTS.md#1-le-bac-à-sable--ce-quune-dll-a-le-droit-de-faire)). **La DLL ne peut
donc pas parler directement à un programme externe.** Son seul canal de sortie est le **log de Deepchart**
(`VAn.AddToLog`), qu'un programme externe relit.

La première version de cette architecture prévoyait un socket local entre la DLL et un moteur Python : c'est
impossible. Proposition ci-dessous, **à valider par Tom**.

> **Mise à jour du 04/11/2026 : le log de Deepchart est chiffré sur disque.** Le « log comme bus » ci-dessous ne
> fonctionne donc pas tel quel. Pistes à étudier quand la sonde aura parlé :
> - **exécution depuis la DLL** par `TradingApi` (ordres, brackets, `FlatAllPositions`), puis réplication vers les
>   autres comptes par un copieur (celui de DeepCharts ou un copieur Rithmic) au lieu du pupitre ;
> - **signal visuel** : la DLL affiche son état sur le graphique, et un petit programme sur le PC le lit à l'écran
>   (comme le pupitre lit déjà ses terminaux par pixels) pour piloter le pupitre ;
> - **recherche** : le moteur tourne dans la DLL pendant le replay et affiche ses statistiques, ou on exporte les
>   séries par une fonction d'export de DeepCharts si elle existe.

## Proposition : moteur C# pur, sur le modèle de `deep`

> **04/11/2026** : Tom a demandé de continuer l'indicateur de setups ; ce montage est donc celui de
> `src/DeepSpxSetups` (moteur dans `Engine/`, vérifié hors de Deepchart par `tests/EngineChecks`). Le backtest en ligne
> de commande reste à écrire. Voir [SETUPS.md](SETUPS.md).

```
Flux de marché ─► DeepCharts desktop (PC Windows)
                   └─ graphique options SPX (symbole ES-CME, 1 min)
                       ├─ indicateurs DeepGamma ─┐ séries lues par GetSeriesValueById
                       └─ DLL deep_spx ◄─────────┘ + trades (OnTick) + carnet (OnBook)
                            └─ Engine/ (C# pur) : features, régime, zones, déclencheurs, risque
                                 │
                                 ├─ dessine sur le graphique (niveaux, signaux, état)
                                 └─ écrit dans le log de Deepchart (lignes DSPX;)
                                        │
                     ┌──────────────────┴──────────────────┐
             Recherche (Python)                    Plus tard : veilleur du log
             extract_dspx.py, études                → pupitre copy trade
```

- **Un seul code de décision**, dans `Engine/` : du C# pur, sans dépendance à Deepchart. Il est compilé dans la DLL
  **et** dans un outil de backtest en ligne de commande qui rejoue les `.tdatac` et les enregistrements du log. C'est
  exactement le montage de `deep` (`src/OrderFlowScalper/Engine` + `tools/Backtest`), qui a produit les mêmes trades dans
  l'étude et dans le moteur de l'indicateur.
- **Python pour explorer** : statistiques, faits connus, graphiques. Une règle retenue est ensuite écrite dans
  `Engine/` et revalidée par le backtest C#, jamais maintenue en double.
- **Le log comme bus** : la DLL y écrit ses enregistrements (features par minute, séries DeepGamma, carnet résumé,
  signaux). En recherche, `tools/extract_dspx.py` le range en CSV. En live, un petit veilleur externe le suit et
  transmettra plus tard les signaux au pupitre. La latence de ce canal (vitesse d'écriture du log sur disque) est à
  mesurer ; sur un horizon de minutes, quelques secondes suffisent.

## Une seule DLL, la nôtre

DeepCharts est l'application hôte. DeepGamma est un ensemble d'indicateurs de DeepCharts, posés sur le même graphique ;
on ne touche pas à leur code. Notre DLL demande au graphique la valeur de la série X de l'indicateur Y.
`VolSysAPI.dll` et `VolumetricaCore.dll` sont les références de compilation (l'API), pas des données.

## Même chaîne en backtest, en simulation et en live

En Deep Replay, la DLL tourne comme en live (`VAn.IsReplayData` le dit), et le moteur `Engine/` produit les mêmes
enregistrements. Le replay sert donc à la fois de générateur d'historique (features + DeepGamma) et de test de toute la
chaîne. L'outil de backtest rejoue les mêmes données hors de Deepchart.

## Fonctionnement continu

- **Démarrage** : au chargement, la DLL calcule l'historique du graphique pour remplir les percentiles ; signaux
  seulement en temps réel.
- **Garde-fou** : plus de nouveaux trades si aucun trade n'arrive en séance, si DeepGamma ne bouge plus, ou s'il vaut
  0 en live (issue #4). Dans l'indicateur v1 : régime inconnu si la dernière valeur valide a plus de 15 barres ou si
  rien n'a bougé depuis 45 barres de séance (DeepGamma ne se met pas forcément à jour chaque minute ; seuil à revoir
  après le test B).
- **Journal** : chaque enregistrement est horodaté en heure de bourse ; une journée se rejoue à l'identique.
- **Poste** : DeepCharts ouvert, mise en veille et redémarrages Windows Update coupés pendant la séance. À terme, un VPS
  Windows.

## Le pupitre : en dernier

Branché une fois la stratégie validée, par le veilleur du log. Jusque-là, exécution simulée (bid ou ask du tick
suivant, + 1 tick). Les règles du pupitre (BUY/SELL en tout-ou-rien, FLAT jamais bloqué, rien n'écoute ailleurs que sur
127.0.0.1) seront respectées.
