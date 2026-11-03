using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Liste les types et membres externes référencés par une dll, et signale ceux que Deepchart refuse.
// Code de sortie 2 si quelque chose d'interdit est trouvé.

if (args.Length == 0)
{
    Console.WriteLine("Usage : dotnet run --project tools/DllCheck -- chemin\\vers\\OrderFlowScalper.dll [--all]");
    return 1;
}
bool all = args.Contains("--all");

// D'après docs-indicators.deepcharts.com/troubleshooting : réflexion, dynamic, fichiers, réseau, processus, threads,
// tâches et async, lock, unsafe et DllImport sont refusés
string[] forbidden =
{
    "System.Reflection", "System.IO", "System.Net", "System.Threading", "System.Diagnostics.Process",
    "System.Runtime.InteropServices", "System.Dynamic", "Microsoft.CSharp", "System.Runtime.Loader",
    "System.Security", "System.Environment",
};

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();

var referenced = new SortedSet<string>(StringComparer.Ordinal);
foreach (var handle in md.TypeReferences)
    referenced.Add(TypeName(md, handle));
foreach (var handle in md.MemberReferences)
{
    var member = md.GetMemberReference(handle);
    string owner = member.Parent.Kind == HandleKind.TypeReference ? TypeName(md, (TypeReferenceHandle)member.Parent) : "";
    if (owner.Length > 0)
        referenced.Add(owner + "::" + md.GetString(member.Name));
}

// Les attributs d'assemblage (nom, version, et le DeepchartDevId lui-même) sont dans System.Reflection mais toujours
// présents, y compris dans le projet officiel : ils ne sont pas du code et Deepchart les accepte
bool IsAssemblyInfo(string r) => r.StartsWith("System.Reflection.Assembly", StringComparison.Ordinal) && r.Contains("Attribute");
referenced.RemoveWhere(IsAssemblyInfo);

var problems = referenced.Where(r => forbidden.Any(f => r.StartsWith(f + ".", StringComparison.Ordinal) || r.StartsWith(f + "::", StringComparison.Ordinal) || r == f)).ToList();
// Environment n'est pas un espace de noms : System.Environment est un type
problems.AddRange(referenced.Where(r => r.StartsWith("System.Environment", StringComparison.Ordinal) && !problems.Contains(r)));

if (all)
    foreach (var r in referenced)
        Console.WriteLine(r);
if (problems.Count == 0)
{
    Console.WriteLine($"OK : {referenced.Count} références externes, aucune interdite.");
    return 0;
}
Console.WriteLine("INTERDIT par Deepchart :");
foreach (var p in problems.Distinct())
    Console.WriteLine("  " + p);
return 2;

static string TypeName(MetadataReader md, TypeReferenceHandle handle)
{
    var t = md.GetTypeReference(handle);
    string name = md.GetString(t.Name);
    if (t.ResolutionScope.Kind == HandleKind.TypeReference)
        return TypeName(md, (TypeReferenceHandle)t.ResolutionScope) + "+" + name;
    string ns = md.GetString(t.Namespace);
    return ns.Length > 0 ? ns + "." + name : name;
}
