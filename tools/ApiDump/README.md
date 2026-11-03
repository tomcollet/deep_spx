# ApiDump

> Repris de `tomcollet/deep` (tools/ApiDump), avec les mots Option, Gamma, Gex, Greek et Strike ajoutés au résumé.

Liste ce que les dll de Deepchart exposent réellement : types, champs, propriétés, méthodes et signatures. Les dll sont
lues comme des fichiers, rien n'est exécuté, et Deepchart n'a pas besoin de tourner.

Depuis le dossier du dépôt :

```
dotnet run --project tools\ApiDump
```

Si Deepchart n'est pas dans `C:\Program Files\Volumetrica Trading\Deepchart\` :

```
dotnet run --project tools\ApiDump -- "D:\chemin\Deepchart"
```

L'écran affiche le résumé des types utiles à l'indicateur (transactions agrégées, carnet, lecture des autres
indicateurs). L'API complète est écrite dans `deepchart-api.txt`, dans le dossier courant.
