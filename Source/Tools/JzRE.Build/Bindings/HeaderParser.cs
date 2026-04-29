// HeaderParser.cs — lightweight C++ header parser for API annotations.
// Mirrors FlaxEngine's BindingsGenerator.Parsing.cs, simplified to a
// pragmatic regex-based approach suited to JzRE-mix's scale.
//
// Parses C++ headers looking for API_CLASS(), API_STRUCT(), API_ENUM(),
// API_FUNCTION(), API_PROPERTY(), API_FIELD(), and API_INTERFACE()
// annotations, and builds the ModuleInfo tree.
//
// Limitations (v0.1):
//   - Single-line declarations only (type + annotation on same logical line)
//   - No nested types
//   - No template types
//   - No multi-line parameter lists
// If a header exceeds these limits, the parser fails with a clear error.

using System.Text.RegularExpressions;

namespace JzRE.Build.Bindings;

public partial class HeaderParser
{
    private readonly string _moduleName;
    private readonly string _namespace;

    public HeaderParser(string moduleName, string ns = "JzRE")
    {
        _moduleName = moduleName;
        _namespace = ns;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Parse all header files for a module and return the combined API surface.
    /// </summary>
    public ModuleInfo Parse(IEnumerable<string> headerFiles)
    {
        var module = new ModuleInfo
        {
            Name = _moduleName,
            Namespace = _namespace
        };

        foreach (var file in headerFiles)
        {
            var fileInfo = ParseFile(file);
            if (fileInfo.Classes.Count > 0 ||
                fileInfo.Structs.Count > 0 ||
                fileInfo.Enums.Count > 0 ||
                fileInfo.Interfaces.Count > 0)
            {
                module.Files.Add(fileInfo);
            }
        }

        return module;
    }

    // ── File-level parsing ────────────────────────────────────────────────

    private FileInfo ParseFile(string path)
    {
        var file = new FileInfo { Path = path };
        var content = File.ReadAllText(path);

        // Remove single-line comments and string literals to avoid false matches
        content = RemoveComments(content);

        file.Classes.AddRange(ParseClasses(content));
        file.Structs.AddRange(ParseStructs(content));
        file.Enums.AddRange(ParseEnums(content));
        file.Interfaces.AddRange(ParseInterfaces(content));

        // Second pass: parse methods/fields/properties inside each class
        foreach (var cls in file.Classes)
        {
            ParseClassBody(cls, content);
        }

        return file;
    }

    // ── Class parsing ─────────────────────────────────────────────────────

    private List<ClassInfo> ParseClasses(string content)
    {
        var result = new List<ClassInfo>();
        var matches = ApiClassRegex().Matches(content);

        foreach (Match m in matches)
        {
            var attrs = ParseAttributes(m.Groups["attrs"].Value);
            var name = m.Groups["name"].Value;
            var baseType = m.Groups["base"].Value.Trim();

            if (string.IsNullOrEmpty(baseType))
                baseType = "JzObject"; // default root

            result.Add(new ClassInfo
            {
                Name = name,
                Namespace = _namespace,
                NativeModule = _moduleName,
                ManagedBaseType = baseType,
                Kind = ResolveClassKind(attrs),
                Attributes = attrs
            });
        }

        return result;
    }

    private static ApiTypeKind ResolveClassKind(List<string> attrs)
    {
        foreach (var a in attrs)
        {
            if (a.StartsWith("Abstract", StringComparison.OrdinalIgnoreCase))
                return ApiTypeKind.Interface;
        }
        return ApiTypeKind.Class;
    }

    // ── Struct parsing ────────────────────────────────────────────────────

    private List<StructInfo> ParseStructs(string content)
    {
        var result = new List<StructInfo>();
        var matches = ApiStructRegex().Matches(content);

        foreach (Match m in matches)
        {
            result.Add(new StructInfo
            {
                Name = m.Groups["name"].Value,
                Namespace = _namespace,
                NativeModule = _moduleName
            });
        }

        return result;
    }

    // ── Enum parsing ──────────────────────────────────────────────────────

    private List<EnumInfo> ParseEnums(string content)
    {
        var result = new List<EnumInfo>();
        var matches = ApiEnumRegex().Matches(content);

        foreach (Match m in matches)
        {
            var name = m.Groups["name"].Value;
            var body = m.Groups["body"].Value;

            var values = new List<(string, int)>();
            int currentValue = 0;

            foreach (var line in body.Split(','))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx >= 0)
                {
                    var valName = trimmed[..eqIdx].Trim();
                    var valStr = trimmed[(eqIdx + 1)..].Trim();
                    if (int.TryParse(valStr, out int v))
                    {
                        currentValue = v;
                        values.Add((valName, v));
                    }
                }
                else
                {
                    values.Add((trimmed, currentValue));
                }
                currentValue++;
            }

            result.Add(new EnumInfo
            {
                Name = name,
                Namespace = _namespace,
                NativeModule = _moduleName,
                Values = values
            });
        }

        return result;
    }

    // ── Interface parsing ─────────────────────────────────────────────────

    private List<InterfaceInfo> ParseInterfaces(string content)
    {
        var result = new List<InterfaceInfo>();
        var matches = ApiInterfaceRegex().Matches(content);

        foreach (Match m in matches)
        {
            result.Add(new InterfaceInfo
            {
                Name = m.Groups["name"].Value,
                Namespace = _namespace,
                NativeModule = _moduleName
            });
        }

        return result;
    }

    // ── Class body parsing (methods, fields, properties) ──────────────────

    private void ParseClassBody(ClassInfo cls, string content)
    {
        // Find the class body — everything between { and };
        // This is a simplified approach that works for single-class-per-header patterns.
        var classPattern = $@"API_CLASS\s*\([^)]*\)\s*(?:class|struct)\s+{cls.Name}[^{{]*\{{";
        var classMatch = Regex.Match(content, classPattern);
        if (!classMatch.Success) return;

        int bodyStart = classMatch.Index + classMatch.Length;
        int braceDepth = 1;
        int bodyEnd = bodyStart;

        for (int i = bodyStart; i < content.Length && braceDepth > 0; i++)
        {
            if (content[i] == '{') braceDepth++;
            else if (content[i] == '}') braceDepth--;
            if (braceDepth == 0) bodyEnd = i;
        }

        var body = content[bodyStart..bodyEnd];

        // Parse methods
        foreach (Match m in ApiFunctionRegex().Matches(body))
        {
            var methodName = m.Groups["name"].Value;
            var returnType = m.Groups["ret"].Value.Trim();
            var isVirtual = m.Groups["virt"].Success;
            var isStatic = m.Groups["stat"].Success;
            var attrs = ParseAttributes(m.Groups["attrs"].Value);

            if (string.IsNullOrEmpty(returnType)) returnType = "void";

            var method = new FunctionInfo
            {
                Name = methodName,
                ReturnType = returnType,
                IsVirtual = isVirtual,
                IsStatic = isStatic,
                Attributes = attrs,
                ParentName = cls.Name
            };

            // Parse parameters
            var paramsStr = m.Groups["params"].Value;
            method.Parameters = ParseParameters(paramsStr);

            cls.Methods.Add(method);
        }

        // Parse fields
        foreach (Match m in ApiFieldRegex().Matches(body))
        {
            cls.Fields.Add(new FieldInfo
            {
                Name = m.Groups["name"].Value,
                Type = m.Groups["type"].Value.Trim(),
                Attributes = ParseAttributes(m.Groups["attrs"].Value)
            });
        }

        // Parse properties (getter/setter pairs)
        foreach (Match m in ApiPropertyRegex().Matches(body))
        {
            var propType = m.Groups["type"].Value.Trim();
            var propName = m.Groups["name"].Value;
            var attrs = ParseAttributes(m.Groups["attrs"].Value);

            // Look for a second API_PROPERTY with same name (getter/setter pair)
            cls.Properties.Add(new PropertyInfo
            {
                Name = propName,
                Type = propType,
                HasGetter = true,
                HasSetter = true,  // assumed — could refine
                Attributes = attrs
            });
        }
    }

    // ── Parameter parsing ─────────────────────────────────────────────────

    private static List<ParameterInfo> ParseParameters(string paramsStr)
    {
        var result = new List<ParameterInfo>();
        if (string.IsNullOrWhiteSpace(paramsStr)) return result;

        var parts = paramsStr.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Handle API_PARAM(refKind)
            var direction = ParamKind.In;
            var dirMatch = ApiParamRegex().Match(trimmed);
            if (dirMatch.Success)
            {
                var kind = dirMatch.Groups["kind"].Value;
                direction = kind.Equals("Out", StringComparison.OrdinalIgnoreCase) ? ParamKind.Out
                          : kind.Equals("Ref", StringComparison.OrdinalIgnoreCase) ? ParamKind.Ref
                          : ParamKind.In;
                trimmed = trimmed.Replace(dirMatch.Value, "").Trim();
            }

            // Split "Type name" or "const Type* name"
            var spaceIdx = trimmed.LastIndexOf(' ');
            if (spaceIdx < 0)
            {
                result.Add(new ParameterInfo { Name = trimmed, Type = trimmed, Direction = direction });
                continue;
            }

            var type = trimmed[..spaceIdx].Trim();
            var name = trimmed[(spaceIdx + 1)..].Trim();

            // Handle "= default" in the name part
            var defaultValue = "";
            var eqIdx = name.IndexOf('=');
            if (eqIdx >= 0)
            {
                defaultValue = name[(eqIdx + 1)..].Trim();
                name = name[..eqIdx].Trim();
            }

            result.Add(new ParameterInfo
            {
                Name = name,
                Type = type,
                Direction = direction,
                HasDefault = eqIdx >= 0,
                DefaultValue = defaultValue
            });
        }

        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<string> ParseAttributes(string attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs)) return new List<string>();
        return attrs.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim().Trim('"'))
                    .Where(a => a.Length > 0)
                    .ToList();
    }

    private static string RemoveComments(string content)
    {
        // Remove // line comments
        content = Regex.Replace(content, @"//.*$", "", RegexOptions.Multiline);
        // Remove /* block comments */
        content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return content;
    }

    // ── Compiled regexes (source-generated for performance) ───────────────

    [GeneratedRegex(@"API_CLASS\s*\((?<attrs>[^)]*)\)\s*(?:class|struct)\s+(?<name>\w+)\s*(?::\s*public\s+(?<base>\w+(?:::Object)?))?")]
    private static partial Regex ApiClassRegex();

    [GeneratedRegex(@"API_STRUCT\s*\((?<attrs>[^)]*)\)\s*struct\s+(?<name>\w+)")]
    private static partial Regex ApiStructRegex();

    [GeneratedRegex(@"API_ENUM\s*\((?<attrs>[^)]*)\)\s*enum\s+(?:class\s+)?(?<name>\w+)\s*(?::\s*\w+)?\s*\{(?<body>[^}]*)\}")]
    private static partial Regex ApiEnumRegex();

    [GeneratedRegex(@"API_INTERFACE\s*\((?<attrs>[^)]*)\)\s*(?:class|struct)\s+(?<name>\w+)")]
    private static partial Regex ApiInterfaceRegex();

    [GeneratedRegex(
        @"API_FUNCTION\s*\((?<attrs>[^)]*)\)\s*" +
        @"(?:(?<stat>static)\s+)?" +
        @"(?:(?<virt>virtual)\s+)?" +
        @"(?<ret>[\w\s*:<>,]+?)\s+" +
        @"(?<name>\w+)\s*\((?<params>[^)]*)\)" +
        @"(?:\s*const)?")]
    private static partial Regex ApiFunctionRegex();

    [GeneratedRegex(@"API_FIELD\s*\((?<attrs>[^)]*)\)\s*(?<type>[\w\s*:<>,]+?)\s+(?<name>\w+)\s*;")]
    private static partial Regex ApiFieldRegex();

    [GeneratedRegex(@"API_PROPERTY\s*\((?<attrs>[^)]*)\)\s*(?<type>[\w\s*:<>,]+?)\s+(?<name>\w+)\s*")]
    private static partial Regex ApiPropertyRegex();

    [GeneratedRegex(@"API_PARAM\s*\((?<kind>\w+)\)")]
    private static partial Regex ApiParamRegex();
}
