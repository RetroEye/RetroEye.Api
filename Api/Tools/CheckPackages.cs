// Checks the app API's NuGet packages before they are published: that RetroEye itself never rides along and that the
// libraries are the plain, readable public surface they are meant to be.
//
//   dotnet run Api\Tools\CheckPackages.cs -- Api\artifacts [--words FILE]...
//
// --words names a file of words no package may hold (one a line, or "Name: word, word, ..."; # starts a comment). The
// list of RetroEye's internal names is kept privately with RetroEye and given here when RetroEye's releases are
// checked; without it the checks below still run, with no words of their own.
//
// For every RetroEye.*.nupkg in the folder (and its .snupkg) it fails (exit code 1) when:
//  * the package holds anything but its nuspec, README.md, LICENSE, icon.png and lib\<tfm>\<id>.dll/.xml; above all
//    RetroEye.exe or any other .exe, or any file whose name holds one of the words;
//  * a DLL defines or refers to a namespace or type holding one of the words, references the RetroEye assembly or one
//    named with a word, or holds one of the words in its names or strings;
//  * a DLL looks obfuscated or stripped: an obfuscator's attribute, a public name that isn't a plain identifier, a
//    public type outside the library's own namespace, a public type missing from the XML documentation, no XML
//    documentation, or a missing well-known type;
//  * the assembly version isn't 1.0.0.0 (kept for all of 1.x), the licence isn't MIT, or RetroEye.Theming.Wpf's
//    dependency on RetroEye.Api isn't a range that stops before 2.0.0;
//  * the .snupkg is missing or lacks a PDB for a DLL.
// It prints what it checked and every problem found. Used by the publishing workflow (.github\workflows\api-nuget.yml).
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false

using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string folder = Path.Combine("Api", "artifacts");
var forbidden = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--words" && i + 1 < args.Length)
    {
        foreach (string raw in File.ReadAllLines(args[++i]))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // "Name: word, word" lists a group's words; a line without a colon is one word
            string words = line.Contains(": ") ? line.Substring(line.IndexOf(": ") + 2) : line;
            forbidden.AddRange(words.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0));
        }
    }
    else folder = args[i];
}
// Whether a text holds one of the forbidden words, and which
string? Forbidden(string text) => forbidden.FirstOrDefault(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
Console.WriteLine($"Checking for {forbidden.Count} forbidden words");
var problems = new List<string>();
var packages = Directory.GetFiles(folder, "RetroEye.*.nupkg");
if (packages.Length == 0)
{
    Console.WriteLine($"No RetroEye.*.nupkg in {Path.GetFullPath(folder)}");
    return 1;
}

// The package ids that may be published, each with types it must contain (a check that names weren't changed)
var known = new Dictionary<string, string[]>
{
    ["RetroEye.Api"] = ["RetroEye.Api.RetroEyeClient", "RetroEye.Api.ThemeState", "RetroEye.Api.Notification", "RetroEye.Api.ApiJson", "RetroEye.Api.ApiNames"],
    ["RetroEye.Theming.Wpf"] = ["RetroEye.Theming.Wpf.RetroWindow", "RetroEye.Theming.Wpf.RetroEyeTheme", "RetroEye.Theming.Wpf.RetroEyeKeys"],
};
var identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(`\d+)?$");

foreach (string package in packages)
{
    string name = Path.GetFileName(package);
    using var zip = ZipFile.OpenRead(package);
    var nuspecEntry = zip.Entries.SingleOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    if (nuspecEntry == null) { problems.Add($"{name}: no nuspec"); continue; }
    XDocument nuspec;
    using (var s = nuspecEntry.Open()) nuspec = XDocument.Load(s);
    XNamespace ns = nuspec.Root!.Name.Namespace;
    var metadata = nuspec.Root.Element(ns + "metadata")!;
    string id = metadata.Element(ns + "id")?.Value ?? "";
    Console.WriteLine($"{name}: {id} {metadata.Element(ns + "version")?.Value}");
    if (!known.TryGetValue(id, out string[]? mustHave)) { problems.Add($"{name}: unknown package id '{id}' (only {string.Join(", ", known.Keys)} are published)"); continue; }
    var license = metadata.Element(ns + "license");
    if (license?.Attribute("type")?.Value != "expression" || license.Value != "MIT") problems.Add($"{name}: the licence isn't the MIT expression");
    foreach (var dep in metadata.Descendants(ns + "dependency"))
    {
        string depId = dep.Attribute("id")?.Value ?? "", range = dep.Attribute("version")?.Value ?? "";
        if (!known.ContainsKey(depId)) problems.Add($"{name}: depends on '{depId}', which isn't one of the app API packages");
        else if (!Regex.IsMatch(range, @"^\[1\.\d+\.\d+, ?2\.0\.0\)$")) problems.Add($"{name}: depends on {depId} '{range}', not [1.x, 2.0.0)");
    }
    if (id == "RetroEye.Theming.Wpf" && !metadata.Descendants(ns + "dependency").Any(d => d.Attribute("id")?.Value == "RetroEye.Api"))
        problems.Add($"{name}: no dependency on RetroEye.Api");

    // Every file must be one the package is meant to have
    var dlls = new List<ZipArchiveEntry>();
    foreach (var entry in zip.Entries)
    {
        string path = entry.FullName, file = Path.GetFileName(path);
        Console.WriteLine($"  {path}");
        if (Forbidden(file) != null || file.Equals("RetroEye.exe", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            problems.Add($"{name}: holds {path}: RetroEye itself must never be in a package");
        bool allowed = path == "[Content_Types].xml" || path.StartsWith("_rels/") || path.StartsWith("package/services/metadata/")
            || entry == nuspecEntry || path is "README.md" or "LICENSE" or "icon.png"
            || Regex.IsMatch(path, $@"^lib/[a-z0-9.\-]+/{Regex.Escape(id)}\.(dll|xml)$");
        if (!allowed) problems.Add($"{name}: unexpected file {path}");
        if (path.StartsWith("lib/") && path.EndsWith(".dll")) dlls.Add(entry);
    }
    if (dlls.Count == 0) problems.Add($"{name}: no library in lib/");

    // The symbols package beside it: a PDB for every DLL
    string snupkg = Path.ChangeExtension(package, ".snupkg");
    HashSet<string> pdbs = [];
    if (File.Exists(snupkg))
    {
        using var symbols = ZipFile.OpenRead(snupkg);
        foreach (var entry in symbols.Entries)
        {
            string file = Path.GetFileName(entry.FullName);
            if (Forbidden(file) != null || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{Path.GetFileName(snupkg)}: holds {entry.FullName}: RetroEye itself must never be in a package");
            if (entry.FullName.EndsWith(".pdb")) pdbs.Add(entry.FullName);
        }
    }
    else problems.Add($"{name}: no {Path.GetFileName(snupkg)} beside it");

    foreach (var dllEntry in dlls)
    {
        string where = $"{name}: {dllEntry.FullName}";
        if (!pdbs.Contains(Path.ChangeExtension(dllEntry.FullName, ".pdb"))) problems.Add($"{where}: no PDB in the symbols package");
        var xmlEntry = zip.GetEntry(Path.ChangeExtension(dllEntry.FullName, ".xml"));
        HashSet<string> documented = [];
        if (xmlEntry == null) problems.Add($"{where}: no XML documentation beside it");
        else
        {
            using var s = xmlEntry.Open();
            foreach (var member in XDocument.Load(s).Descendants("member"))
                documented.Add(member.Attribute("name")?.Value ?? "");
        }

        byte[] bytes;
        using (var s = dllEntry.Open()) using (var copy = new MemoryStream()) { s.CopyTo(copy); bytes = copy.ToArray(); }
        using var pe = new PEReader(new MemoryStream(bytes));
        MetadataReader md;
        try { md = pe.GetMetadataReader(); }
        catch (Exception e) when (e is BadImageFormatException or InvalidOperationException)
        {
            problems.Add($"{where}: not a .NET assembly ({e.Message})");
            continue;
        }
        var assembly = md.GetAssemblyDefinition();
        string assemblyName = md.GetString(assembly.Name);
        if (assemblyName != id) problems.Add($"{where}: assembly {assemblyName}, not {id}");
        if (assembly.Version != new Version(1, 0, 0, 0)) problems.Add($"{where}: assembly version {assembly.Version}, not 1.0.0.0");

        foreach (var handle in md.AssemblyReferences)
        {
            string reference = md.GetString(md.GetAssemblyReference(handle).Name);
            if (Forbidden(reference) != null || reference.Equals("RetroEye", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{where}: references the assembly {reference}");
        }
        foreach (var handle in md.GetCustomAttributes(EntityHandle.AssemblyDefinition))
        {
            string attribute = AttributeName(md, md.GetCustomAttribute(handle));
            if (Regex.IsMatch(attribute, "Obfusc|Dotfuscator|ConfusedBy|SmartAssembly|Babel|Eazfuscator", RegexOptions.IgnoreCase))
                problems.Add($"{where}: marked by an obfuscator ({attribute})");
        }

        var publicTypes = new List<string>();
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);
        int types = 0;
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            string typeNs = md.GetString(type.Namespace), typeName = md.GetString(type.Name);
            string full = FullName(md, type);
            types++;
            if (!type.IsNested) namespaces.Add(typeNs.Length == 0 ? "(global)" : typeNs);
            if (Forbidden(full) != null) problems.Add($"{where}: defines {full}");
            var visibility = type.Attributes & System.Reflection.TypeAttributes.VisibilityMask;
            bool isPublic = visibility == System.Reflection.TypeAttributes.Public
                || (visibility is System.Reflection.TypeAttributes.NestedPublic or System.Reflection.TypeAttributes.NestedFamily or System.Reflection.TypeAttributes.NestedFamORAssem
                    && IsVisible(md, type));
            if (!isPublic) continue;
            publicTypes.Add(full);
            string rootNs = type.IsNested ? md.GetString(md.GetTypeDefinition(type.GetDeclaringType()).Namespace) : typeNs;
            if (rootNs != id && !rootNs.StartsWith(id + ".")) problems.Add($"{where}: public type {full} outside the namespace {id}");
            if (!identifier.IsMatch(typeName)) problems.Add($"{where}: public type name '{typeName}' isn't a plain identifier (obfuscated?)");
            if (xmlEntry != null && !documented.Contains("T:" + full.Replace('/', '.'))) problems.Add($"{where}: public type {full} has no XML documentation (stripped or renamed?)");
            foreach (var m in type.GetMethods())
            {
                var method = md.GetMethodDefinition(m);
                var access = method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask;
                if (access is not (System.Reflection.MethodAttributes.Public or System.Reflection.MethodAttributes.Family or System.Reflection.MethodAttributes.FamORAssem)) continue;
                string methodName = md.GetString(method.Name);
                if (methodName is ".ctor" or ".cctor") continue;
                // Accessors (get_X, add_X, op_X) are plain identifiers too; explicit interface members contain dots
                if (!identifier.IsMatch(methodName) && !methodName.Contains('.')) problems.Add($"{where}: public member {full}.{methodName} isn't a plain identifier (obfuscated?)");
            }
        }
        foreach (var handle in md.TypeReferences)
        {
            var reference = md.GetTypeReference(handle);
            string full = md.GetString(reference.Namespace) + "." + md.GetString(reference.Name);
            if (Forbidden(full) != null) problems.Add($"{where}: refers to the type {full}");
        }
        // Strings in the code (the user string heap) and every name in the metadata (the string heap)
        var text = new System.Text.StringBuilder();
        for (var h = md.GetNextHandle(MetadataTokens.UserStringHandle(0)); !h.IsNil; h = md.GetNextHandle(h)) text.Append(md.GetUserString(h)).Append('\n');
        for (var h = md.GetNextHandle(MetadataTokens.StringHandle(0)); !h.IsNil; h = md.GetNextHandle(h)) text.Append(md.GetString(h)).Append('\n');
        if (Forbidden(text.ToString()) is string word) problems.Add($"{where}: the forbidden word '{word}' is in its names or strings");
        foreach (string must in mustHave)
            if (!publicTypes.Contains(must)) problems.Add($"{where}: the public type {must} is missing (renamed or stripped?)");
        Console.WriteLine($"  {dllEntry.FullName}: {assemblyName} {assembly.Version}, {types} types, {publicTypes.Count} public, {documented.Count} documented members");
        Console.WriteLine($"    namespaces: {string.Join(", ", namespaces)}");
    }
}

if (problems.Count == 0)
{
    Console.WriteLine($"OK: {packages.Length} packages checked, no RetroEye internals, readable and documented.");
    return 0;
}
Console.WriteLine($"FAILED: {problems.Count} problems");
foreach (string problem in problems) Console.WriteLine("  " + problem);
return 1;

// A type's full name as the XML documentation and the baselines write it (nested types with '/')
static string FullName(MetadataReader md, TypeDefinition type)
{
    if (type.IsNested) return FullName(md, md.GetTypeDefinition(type.GetDeclaringType())) + "/" + md.GetString(type.Name);
    string ns = md.GetString(type.Namespace);
    return ns.Length == 0 ? md.GetString(type.Name) : ns + "." + md.GetString(type.Name);
}

// Whether a nested type can be seen from outside: it and every type around it are public
static bool IsVisible(MetadataReader md, TypeDefinition type)
{
    while (type.IsNested)
    {
        type = md.GetTypeDefinition(type.GetDeclaringType());
        var visibility = type.Attributes & System.Reflection.TypeAttributes.VisibilityMask;
        if (visibility is not (System.Reflection.TypeAttributes.Public or System.Reflection.TypeAttributes.NestedPublic)) return false;
    }
    return true;
}

// The name of a custom attribute's type
static string AttributeName(MetadataReader md, CustomAttribute attribute)
{
    if (attribute.Constructor.Kind == HandleKind.MemberReference)
    {
        var parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind == HandleKind.TypeReference) return md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name);
    }
    else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
    {
        var type = md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
        return md.GetString(type.Name);
    }
    return "";
}
