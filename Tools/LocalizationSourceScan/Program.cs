using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Contains("--self-test"))
{
    var sample = Scan("Presentation/Sample.cs", "class C { void M() { label.text = $\"Мана {mana:0} / {max}\"; Debug.LogWarning(\"Ошибка\"); const string id = \"Имя\"; } }");
    if (sample.Count != 3 || sample.Single(x => x.Kind == "template").Placeholders.Count != 2 || sample.Count(x => x.Exclusion != null) != 2)
        throw new Exception("Source scanner self-test failed.");
    Console.WriteLine("PASS: interpolation, formats, diagnostics and constants.");
    return;
}
var positional = args.Where(x => !x.StartsWith("--")).ToArray();
var repository = Path.GetFullPath(positional.Length > 0 ? positional[0] : ".");
var destination = Path.GetFullPath(positional.Length > 1 ? positional[1] : Path.Combine(repository, "Tools/LocalizationSourceScan/source-manifest.json"));
var sourceRoot = Path.Combine(repository, "RemainsUnity/Assets/Remains");
if (args.Contains("--rewrite"))
{
    foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceRoot, "Presentation"), "*.cs").Concat(Directory.EnumerateFiles(Path.Combine(sourceRoot,"Gameplay"),"*.cs")).Append(Path.Combine(sourceRoot,"Runtime/PrototypeRuntime.cs")).Order(StringComparer.Ordinal))
    {
        if (Path.GetFileName(file) is "LocalizedUiText.cs" or "LocalizationFonts.cs") continue;
        var source = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(source);
        var relative = Path.GetRelativePath(repository, file).Replace('\\', '/');
        var candidates = Scan(relative, source).Where(x => x.Exclusion == null).ToDictionary(x => x.Start);
        var root = tree.GetRoot();
        var nodes = root.DescendantNodes().Where(n => candidates.ContainsKey(n.SpanStart) &&
            !n.Ancestors().OfType<InvocationExpressionSyntax>().Any(i => i.Expression.ToString().Contains("GameLocalization.") && i.ArgumentList.Arguments.Any(a => a.Expression == n)) &&
            !n.Ancestors().OfType<ParameterSyntax>().Any() &&
            SafeUiContext(n, Path.GetFileNameWithoutExtension(file)) &&
            (n is LiteralExpressionSyntax || n is InterpolatedStringExpressionSyntax)).ToArray();
        var changedRoot = root.ReplaceNodes(nodes, (original, rewritten) =>
        {
            var entry = candidates[original.SpanStart];
            var call = $"Remains.Content.GameLocalization.{(entry.Kind == "template" ? "Format" : "UiText")}({SymbolDisplay.FormatLiteral(entry.Key, true)}, {rewritten.WithoutTrivia()}, {SymbolDisplay.FormatLiteral(relative, true)})";
            if (Path.GetFileName(file)=="KeyboardBindings.cs" && original.Ancestors().OfType<VariableDeclaratorSyntax>().Any(v=>v.Identifier.ValueText=="definitions")) call="()=>"+call;
            return SyntaxFactory.ParseExpression(call).WithTriviaFrom(original);
        });
        var factoryArgs = changedRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i =>
            i.Expression.ToString().EndsWith("GameLocalization.UiText") && i.Parent is ArgumentSyntax a &&
            a.Parent?.Parent is InvocationExpressionSyntax factory && factory.Expression.ToString() is "ui.Label" or "ui.Button" or "CreateButton").ToArray();
        var changed = changedRoot.ReplaceNodes(factoryArgs,(original,rewritten)=>SyntaxFactory.ParseExpression("()=>"+rewritten.WithoutTrivia()).WithTriviaFrom(original)).ToFullString();
        if (CSharpSyntaxTree.ParseText(changed).GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error) > tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error))
            throw new Exception($"Rewrite introduced parse errors: {relative}");
        if (changed != source) { File.WriteAllText(file, changed, new UTF8Encoding(false)); Console.WriteLine($"Rewrote {nodes.Length}: {relative}"); }
    }
}
var strings = new List<SourceString>();
foreach (var folder in new[] { "Presentation", "Runtime", "Gameplay" })
foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceRoot, folder), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    strings.AddRange(Scan(Path.GetRelativePath(repository, file).Replace('\\', '/'), File.ReadAllText(file)));
var entries = strings.Where(x => x.Exclusion == null).GroupBy(x => x.Key).Select(group => new
{
    key = group.Key, russian = group.First().Russian, kind = group.First().Kind, registered = group.Any(x => x.Registered),
    placeholders = group.First().Placeholders.Select(p => new { p.Index, p.Alignment, p.Format }),
    locations = group.Select(x => new { x.File, x.Line, x.Context, x.Expressions })
}).OrderBy(x => x.key).ToArray();
var manifest = new { schemaVersion = 1, sourceLanguage = "ru", entries, excluded = strings.Where(x => x.Exclusion != null).OrderBy(x => x.File).ThenBy(x => x.Line) };
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
File.WriteAllText(destination, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new UTF8Encoding(false));
Console.WriteLine($"{entries.Length} entries, {strings.Count(x => x.Exclusion != null)} excluded; {destination}");

static List<SourceString> Scan(string file, string source)
{
    var result = new List<SourceString>();
    var tree = CSharpSyntaxTree.ParseText(source);
    foreach (var node in tree.GetRoot().DescendantNodes().Where(n => n is InterpolatedStringExpressionSyntax || n is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression)))
    {
        var placeholders = new List<Placeholder>();
        var expressions = new List<string>();
        string russian;
        if (node is InterpolatedStringExpressionSyntax interpolation)
        {
            var text = new StringBuilder();
            foreach (var part in interpolation.Contents)
            {
                if (part is InterpolatedStringTextSyntax literal) text.Append(literal.TextToken.ValueText.Replace("{", "{{").Replace("}", "}}"));
                else if (part is InterpolationSyntax value)
                {
                    var index = expressions.Count;
                    expressions.Add(value.Expression.ToString());
                    var alignment = value.AlignmentClause?.Value.ToString();
                    var format = value.FormatClause?.FormatStringToken.ValueText;
                    placeholders.Add(new(index, alignment, format));
                    text.Append('{').Append(index);
                    if (alignment != null) text.Append(',').Append(alignment);
                    if (format != null) text.Append(':').Append(format);
                    text.Append('}');
                }
            }
            russian = text.ToString();
        }
        else russian = ((LiteralExpressionSyntax)node).Token.ValueText;
        if (!Regex.IsMatch(russian, "[А-Яа-яЁё]")) continue;
        var context = node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()?.ToString().Split('{')[0].Trim()
            ?? node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "file";
        var registration = node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault(i => i.Expression.ToString().Contains("GameLocalization.") && i.ArgumentList.Arguments.Count >= 2 && i.ArgumentList.Arguments[1].Expression == node);
        var registeredKey = registration?.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax;
        var exclusion = file.Contains("/Runtime/", StringComparison.Ordinal) && Path.GetFileName(file) is not ("PrototypeRuntime.cs" or "ProjectStorage.cs")
            ? "runtime probe/diagnostic harness" : Exclusion(node);
        if (registeredKey != null) exclusion = null;
        var stem = Path.GetFileNameWithoutExtension(file);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(russian))).Substring(0, 16).ToLowerInvariant();
        result.Add(new(registeredKey?.Token.ValueText ?? $"source.{stem}.{hash}", russian, node is InterpolatedStringExpressionSyntax ? "template" : "text", file,
            tree.GetLineSpan(node.Span).StartLinePosition.Line + 1, context, placeholders, expressions, exclusion, node.SpanStart, registeredKey != null));
    }
    return result;
}

static string? Exclusion(SyntaxNode node)
{
    if (node.Ancestors().OfType<AttributeSyntax>().Any()) return "attribute/editor metadata";
    if (node.Ancestors().Any(x => x is ThrowStatementSyntax || x is ThrowExpressionSyntax)) return "exception";
    if (node.Ancestors().OfType<VariableDeclarationSyntax>().Any(v => v.Parent is FieldDeclarationSyntax f && f.Modifiers.Any(SyntaxKind.ConstKeyword) || v.Parent is LocalDeclarationStatementSyntax l && l.Modifiers.Any(SyntaxKind.ConstKeyword))) return "constant: inspect before localization";
    if (node.Ancestors().Any(x => x is PatternSyntax || x is CaseSwitchLabelSyntax)) return "pattern/matching value";
    foreach (var invocation in node.Ancestors().OfType<InvocationExpressionSyntax>())
    {
        var name = invocation.Expression.ToString();
        if (name.Contains("Debug.") || name.EndsWith("Log") || name.EndsWith("LogWarning") || name.EndsWith("LogError")) return "developer diagnostic";
        if (name.Contains("GameLocalization.") && invocation.ArgumentList.Arguments.Any(a => a.Expression == node)) return "already localized";
    }
    if (node.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(x => x.Type.ToString().EndsWith("Exception"))) return "exception";
    if (node.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(x => x.Type.ToString() == "GameObject")) return "object name";
    return null;
}

static bool SafeUiContext(SyntaxNode node, string file)
{
    // These classes format player-facing inventory/interaction/save labels. Their logging,
    // exceptions and constants are excluded before this predicate is called.
    var labelClasses = new HashSet<string> { "ArmorProtectionDescription", "CharacterLibraryPanel", "FloatingContainerPanel", "InteractionHint", "PersonalInventoryPanel", "SelectedItemHudPresentation", "WorldLobbyPanel", "WorldLobbyView", "WorldMapPanel", "WorldPickerPanel", "WorldPickerView", "MainMenuPanel", "AudioDspBuffer", "AudioPreferences", "BodyTelekinesisController", "CombatController", "GrenadeTelekinesis", "ItemShortcutActions", "LooseItemPickup", "MechanismInteraction", "MineTelekinesis", "RevivalController", "TelekinesisController", "WeaponReloadController" };
    foreach (var call in node.Ancestors().OfType<InvocationExpressionSyntax>())
    {
        var name = call.Expression.ToString();
        if (name.Contains("FrameHitchLog") || name.EndsWith(".Name") || name.EndsWith(".Description") || name.EndsWith("IdentityLabel") || name.EndsWith("GameAssets.Load") || name=="Failure") return false;
        if (name.EndsWith(".ShowNotice") || name.EndsWith(".ShowMessage") || name=="EndFailedSession") return true;
        if (name is "ui.Label" or "ui.BackedLabel" or "ui.Button" or "CreateButton" or "SetText" or "Notify" or "ShowNotice" or "ShowMessage" or "SetStatus" or "ContentComparisonView.Draw" or "Message" or "selectedWorldPanel.SetPending" or "containerHint.RefreshAction") return true;
    }
    if (file == "KeyboardBindings") return true;
    if (file == "WorldCopyLabels") return node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() is MethodDeclarationSyntax m && m.Identifier.ValueText is "Read" or "ChoiceText" or "SelectedText";
    if (labelClasses.Contains(file)) return true;
    if (file == "CharacterEditorPanel" && node.Ancestors().OfType<VariableDeclaratorSyntax>().Any(v => v.Identifier.ValueText == "names")) return true;
    return node.Ancestors().OfType<AssignmentExpressionSyntax>().Any(a => a.Left is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "text" && a.Right.Span.Contains(node.Span));
}

record Placeholder(int Index, string? Alignment, string? Format);
record SourceString(string Key, string Russian, string Kind, string File, int Line, string Context, List<Placeholder> Placeholders, List<string> Expressions, string? Exclusion, int Start, bool Registered);
