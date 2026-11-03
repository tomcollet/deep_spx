# DllCheck

> Repris de `tomcollet/deep` (tools/DllCheck).

Deepchart refuse une dll d'indicateurs qui utilise de la réflexion, des fichiers, le réseau, des threads, des tâches,
`lock`, de l'interop ou `dynamic`. Il ne dit pas toujours pourquoi : seule une notification « not allowed »
s'affiche. Ce contrôle imite le sien : il liste ce que la dll référence et signale ce qui est interdit.

```
dotnet build tests/ProbeCompileCheck
dotnet run --project tools/DllCheck -- tests/ProbeCompileCheck/bin/Debug/net10.0/ProbeCompileCheck.dll
```

`--all` affiche toutes les références. Exemple de piège attrapé : un indexeur `this[int]` fait référencer
`System.Reflection.DefaultMemberAttribute`, que Deepchart prend pour de la réflexion.
