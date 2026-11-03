using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Liste l'API publique de VolSysAPI.dll et VolumetricaCore.dll, sans les exécuter.
// Écrit tout dans deepchart-api.txt et affiche à l'écran le résumé des types qui nous manquent.

string dir = args.Length > 0 ? args[0] : @"C:\Program Files\Volumetrica Trading\Deepchart\";
string[] targets = { "VolSysAPI.dll", "VolumetricaCore.dll" };
foreach (var t in targets)
    if (!File.Exists(Path.Combine(dir, t)))
    {
        Console.WriteLine($"{t} introuvable dans {dir}");
        Console.WriteLine("Indiquez le dossier de Deepchart : dotnet run --project tools\\ApiDump -- \"C:\\chemin\\Deepchart\"");
        return 1;
    }

// Les références sont résolues dans le dossier de Deepchart puis dans les frameworks .NET installés
var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
void AddDir(string d)
{
    if (!Directory.Exists(d)) return;
    foreach (var f in Directory.GetFiles(d, "*.dll"))
        paths.TryAdd(Path.GetFileName(f), f);
}
AddDir(dir);
string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
AddDir(runtimeDir);
// Frameworks frères (WindowsDesktop, AspNetCore) de la même version ou de la plus récente
var shared = Directory.GetParent(runtimeDir.TrimEnd(Path.DirectorySeparatorChar))?.Parent;
if (shared != null)
    foreach (var fw in new[] { "Microsoft.WindowsDesktop.App", "Microsoft.AspNetCore.App" })
    {
        var fwDir = Path.Combine(shared.FullName, fw);
        if (Directory.Exists(fwDir))
            foreach (var v in Directory.GetDirectories(fwDir).OrderByDescending(x => x))
            {
                AddDir(v);
                break;
            }
    }

using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths.Values), "System.Private.CoreLib");
var full = new StringBuilder();
var focus = new StringBuilder();
string[] focusTypes = { "AggrInfo", "TickByTick", "BookUpdate", "IMethodAPI", "IMethodCloudAPI", "ITradingAPI", "Indicator" };
string[] focusWords = { "Aggr", "Mbo", "Mbp", "Book", "Dom", "Order", "Series", "Params", "Indicator", "Iceberg", "Big", "Trade", "Option", "Gamma", "Gex", "Greek", "Strike" };

foreach (var target in targets)
{
    Assembly asm;
    try { asm = mlc.LoadFromAssemblyPath(Path.Combine(dir, target)); }
    catch (Exception e) { Console.WriteLine($"{target} : {e.Message}"); continue; }
    full.AppendLine($"##### {asm.GetName().Name} {asm.GetName().Version}");
    Type[] types;
    try { types = asm.GetTypes(); }
    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
    foreach (var type in types.Where(t => IsVisible(t)).OrderBy(t => t.FullName))
    {
        string text = Describe(type);
        full.Append(text);
        bool isFocusType = focusTypes.Contains(type.Name);
        bool hasFocusWord = focusWords.Any(w => type.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (isFocusType)
            focus.Append(Describe(type, focusWords));
        else if (hasFocusWord && (type.Namespace ?? "").StartsWith("Volumetrica", StringComparison.Ordinal) | (type.Namespace ?? "").StartsWith("VolSys", StringComparison.Ordinal))
            focus.Append(Describe(type));
    }
}

File.WriteAllText("deepchart-api.txt", full.ToString());
Console.WriteLine(focus.ToString());
Console.WriteLine($"API complète écrite dans {Path.GetFullPath("deepchart-api.txt")}");
return 0;

static bool IsVisible(Type t)
{
    try
    {
        if (t.IsNested) return (t.IsNestedPublic) && IsVisible(t.DeclaringType);
        return t.IsPublic;
    }
    catch { return false; }
}

// Un type et ses membres publics. Avec un filtre, seuls les membres dont le nom contient un des mots sont listés
// (pour les grosses interfaces comme IMethodAPI), plus tous les membres des petits types
static string Describe(Type type, string[] filter = null)
{
    var sb = new StringBuilder();
    string kind;
    try { kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : "class"; }
    catch { kind = "type"; }
    string baseName = "";
    try { if (type.BaseType != null && !type.IsEnum && !type.IsValueType && type.BaseType.FullName != "System.Object") baseName = " : " + Name(type.BaseType); } catch { }
    try
    {
        var ifaces = type.IsEnum ? Array.Empty<Type>() : type.GetInterfaces();
        if (ifaces.Length > 0) baseName += (baseName.Length == 0 ? " : " : ", ") + string.Join(", ", ifaces.Select(Name));
    }
    catch { }
    sb.AppendLine($"{kind} {Name(type)}{baseName}{Attrs(() => type.GetCustomAttributesData())}");

    var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    int members = 0;
    try { members = type.GetMembers(flags).Length; } catch { }
    bool filtered = filter != null && members > 40;

    bool Keep(string name) => !filtered || filter.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    if (type.IsEnum)
    {
        try
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                sb.AppendLine($"    {f.Name} = {f.GetRawConstantValue()}");
        }
        catch (Exception e) { sb.AppendLine($"    (énumération illisible : {e.Message})"); }
        return sb.ToString();
    }
    try
    {
        foreach (var f in type.GetFields(flags).Where(f => Keep(f.Name)))
            sb.AppendLine($"    field {Safe(() => Name(f.FieldType))} {f.Name}{(f.IsStatic ? " (static)" : "")}{Attrs(() => f.GetCustomAttributesData())}");
    }
    catch (Exception e) { sb.AppendLine($"    (champs illisibles : {e.Message})"); }
    try
    {
        foreach (var p in type.GetProperties(flags).Where(p => Keep(p.Name)))
        {
            string access = (p.CanRead ? "get; " : "") + (p.CanWrite && p.SetMethod != null && p.SetMethod.IsPublic ? "set; " : "");
            sb.AppendLine($"    prop {Safe(() => Name(p.PropertyType))} {p.Name} {{ {access}}}{Attrs(() => p.GetCustomAttributesData())}");
        }
    }
    catch (Exception e) { sb.AppendLine($"    (propriétés illisibles : {e.Message})"); }
    try
    {
        foreach (var m in type.GetMethods(flags).Where(m => !m.IsSpecialName && Keep(m.Name)))
            sb.AppendLine($"    {(m.IsStatic ? "static " : "")}{(m.IsVirtual && !type.IsInterface ? "virtual " : "")}{Safe(() => Name(m.ReturnType))} {m.Name}({Params(m)}){Attrs(() => m.GetCustomAttributesData())}");
        foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            sb.AppendLine($"    ctor({Params(c)})");
    }
    catch (Exception e) { sb.AppendLine($"    (méthodes illisibles : {e.Message})"); }
    if (filtered)
        sb.AppendLine($"    ... {members} membres au total, liste complète dans deepchart-api.txt");
    return sb.ToString();
}

static string Params(MethodBase m)
{
    try
    {
        return string.Join(", ", m.GetParameters().Select(p =>
        {
            string s = $"{Safe(() => Name(p.ParameterType))} {p.Name}";
            try { if (p.HasDefaultValue) s += $" = {p.RawDefaultValue ?? "null"}"; } catch { }
            return s;
        }));
    }
    catch (Exception e) { return $"?{e.Message}"; }
}

static string Attrs(Func<IList<CustomAttributeData>> get)
{
    try
    {
        var names = get().Select(a => a.AttributeType.Name.Replace("Attribute", ""))
            .Where(n => n is "DesktopOnly" or "Obsolete" or "SupportedOSPlatform").ToList();
        return names.Count == 0 ? "" : "  [" + string.Join(", ", names) + "]";
    }
    catch { return ""; }
}

static string Name(Type t)
{
    if (t.IsGenericType)
    {
        string n = t.Name;
        int tick = n.IndexOf('`');
        if (tick > 0) n = n[..tick];
        return $"{n}<{string.Join(", ", t.GetGenericArguments().Select(Name))}>";
    }
    if (t.IsArray) return Name(t.GetElementType()) + "[]";
    if (t.IsByRef) return "ref " + Name(t.GetElementType());
    string prefix = t.IsNested && !t.IsGenericParameter ? Name(t.DeclaringType) + "." : "";
    return prefix + t.Name;
}

static string Safe(Func<string> f)
{
    try { return f(); } catch (Exception e) { return $"?({e.GetType().Name})"; }
}
