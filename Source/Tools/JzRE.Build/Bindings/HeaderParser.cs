// HeaderParser.cs — Tokenizer-based C++ header parser for API annotations.
// Mirrors FlaxEngine's BindingsGenerator.Parsing.cs approach:
//   - Character-level Tokenizer (not regex) handles multi-line declarations,
//     inline function bodies, template parameters, and preprocessor lines.
//   - Scoped class body parser recurses on API_FUNCTION / API_PROPERTY / API_FIELD.
//   - API_EXPORT() free functions produce FreeFunctionInfo entries.
//
// Supported annotations:
//   API_CLASS(...)      — class or struct exposed to managed code
//   API_STRUCT(...)     — POD struct
//   API_ENUM(...)       — enum or enum class
//   API_INTERFACE(...)  — abstract interface
//   API_FUNCTION(...)   — method or standalone function inside a class
//   API_PROPERTY(...)   — getter/setter pair
//   API_FIELD(...)      — exposed field
//   API_EXPORT()        — standalone exported C function (free function)
//   API_PARAM(Out/Ref)  — parameter direction hint

using System.Text;

namespace JzRE.Build.Bindings;

public sealed class HeaderParser
{
    private readonly string _moduleName;
    private readonly string _namespace;

    public HeaderParser(string moduleName, string ns = "JzRE")
    {
        _moduleName = moduleName;
        _namespace  = ns;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public ModuleInfo Parse(IEnumerable<string> headerFiles)
    {
        var module = new ModuleInfo
        {
            Name              = _moduleName,
            Namespace         = _namespace,
            NativeLibraryName = _moduleName,
        };

        foreach (var path in headerFiles)
        {
            var fi = ParseFile(path);
            if (fi.Classes.Count > 0 || fi.Structs.Count > 0 || fi.Enums.Count > 0 ||
                fi.Interfaces.Count > 0 || fi.FreeFunctions.Count > 0)
                module.Files.Add(fi);
        }

        return module;
    }

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    private enum TK
    {
        Ident,    // identifier / keyword
        LParen,   // (
        RParen,   // )
        LBrace,   // {
        RBrace,   // }
        Semi,     // ;
        Colon,    // :
        Comma,    // ,
        Star,     // *
        Amp,      // &
        Eq,       // =
        Lt,       // <
        Gt,       // >
        Tilde,    // ~
        Other,
        EOF,
    }

    private readonly record struct Tok(TK Kind, string Text);

    private sealed class Tokenizer
    {
        private readonly string _src;
        private int   _pos;
        private Tok?  _peek;

        public Tokenizer(string src) => _src = src;

        public Tok Peek() => _peek ??= Read();

        public Tok Next()
        {
            var t = _peek ?? Read();
            _peek = null;
            return t;
        }

        private Tok Read()
        {
            while (_pos < _src.Length)
            {
                char c = _src[_pos];
                if (c is '\r' or '\n' or ' ' or '\t') { _pos++; continue; }

                // line comment
                if (c == '/' && Peek2() == '/')
                {
                    while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                    continue;
                }
                // block comment
                if (c == '/' && Peek2() == '*')
                {
                    _pos += 2;
                    while (_pos < _src.Length - 1 && !(_src[_pos] == '*' && _src[_pos + 1] == '/'))
                        _pos++;
                    _pos += 2;
                    continue;
                }
                // preprocessor
                if (c == '#')
                {
                    while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                    continue;
                }
                // string literal — skip entirely to avoid false matches
                if (c == '"')
                {
                    _pos++;
                    while (_pos < _src.Length && _src[_pos] != '"')
                    {
                        if (_src[_pos] == '\\') _pos++;
                        _pos++;
                    }
                    _pos++;
                    continue;
                }
                break;
            }

            if (_pos >= _src.Length) return new Tok(TK.EOF, "");

            char ch = _src[_pos];

            // identifier
            if (char.IsLetter(ch) || ch == '_')
            {
                int s = _pos;
                while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
                    _pos++;
                return new Tok(TK.Ident, _src[s.._pos]);
            }

            // number literal — skip
            if (char.IsDigit(ch))
            {
                int s = _pos;
                while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] is '.' or 'x' or 'X' or 'u' or 'U' or 'l' or 'L' or 'f' or 'F'))
                    _pos++;
                return new Tok(TK.Other, _src[s.._pos]);
            }

            _pos++;

            // scope :: → treat as single Other so we can skip it cleanly
            if (ch == ':' && _pos < _src.Length && _src[_pos] == ':') { _pos++; return new Tok(TK.Other, "::"); }

            return ch switch
            {
                '(' => new Tok(TK.LParen, "("),
                ')' => new Tok(TK.RParen, ")"),
                '{' => new Tok(TK.LBrace, "{"),
                '}' => new Tok(TK.RBrace, "}"),
                ';' => new Tok(TK.Semi,   ";"),
                ':' => new Tok(TK.Colon,  ":"),
                ',' => new Tok(TK.Comma,  ","),
                '*' => new Tok(TK.Star,   "*"),
                '&' => new Tok(TK.Amp,    "&"),
                '=' => new Tok(TK.Eq,     "="),
                '<' => new Tok(TK.Lt,     "<"),
                '>' => new Tok(TK.Gt,     ">"),
                '~' => new Tok(TK.Tilde,  "~"),
                _   => new Tok(TK.Other,  ch.ToString()),
            };
        }

        private char Peek2() => _pos + 1 < _src.Length ? _src[_pos + 1] : '\0';
    }

    // ── File-level parse ──────────────────────────────────────────────────────

    private FileInfo ParseFile(string path)
    {
        var file = new FileInfo { Path = path };
        var tok  = new Tokenizer(File.ReadAllText(path));

        while (tok.Peek().Kind != TK.EOF)
        {
            if (tok.Peek().Kind != TK.Ident) { tok.Next(); continue; }

            switch (tok.Peek().Text)
            {
                case "API_CLASS":     tok.Next(); ParseClass(tok, file);        break;
                case "API_STRUCT":    tok.Next(); ParseStruct(tok, file);       break;
                case "API_ENUM":      tok.Next(); ParseEnum(tok, file);         break;
                case "API_INTERFACE": tok.Next(); ParseInterface(tok, file);    break;
                case "API_EXPORT":    tok.Next(); ParseFreeFunction(tok, file); break;
                default:                          tok.Next();                   break;
            }
        }

        return file;
    }

    // ── API_CLASS ─────────────────────────────────────────────────────────────

    private void ParseClass(Tokenizer tok, FileInfo file)
    {
        var attrs = ConsumeAnnotationArgs(tok);

        // expect class | struct
        if (tok.Peek().Kind != TK.Ident) return;
        var kw = tok.Next().Text;
        if (kw is not ("class" or "struct")) return;

        // skip optional DLLEXPORT / visibility macros (e.g. FLAXENGINE_API)
        while (tok.Peek().Kind == TK.Ident && IsVisibilityMacro(tok.Peek().Text))
            tok.Next();

        if (tok.Peek().Kind != TK.Ident) return;
        var name = tok.Next().Text;

        var isStatic = attrs.Any(a => a.Equals("Static", StringComparison.OrdinalIgnoreCase));

        var cls = new ClassInfo
        {
            Name           = name,
            Namespace      = _namespace,
            NativeModule   = _moduleName,
            Attributes     = attrs,
            IsStaticClass  = isStatic,
            NeedsManagedPeer = !isStatic,
        };

        // optional base class:  : public BaseClass
        if (tok.Peek().Kind == TK.Colon)
        {
            tok.Next(); // :
            SkipAccessSpecifiers(tok);
            if (tok.Peek().Kind == TK.Ident)
                cls.ManagedBaseType = tok.Next().Text;
        }

        // skip to opening brace
        while (tok.Peek().Kind != TK.LBrace && tok.Peek().Kind != TK.EOF) tok.Next();
        if (tok.Peek().Kind == TK.EOF) return;
        tok.Next(); // {

        ParseClassBody(tok, cls);
        file.Classes.Add(cls);
    }

    private void ParseClassBody(Tokenizer tok, ClassInfo cls)
    {
        int depth = 1;
        while (tok.Peek().Kind != TK.EOF && depth > 0)
        {
            var t = tok.Peek();

            if (t.Kind == TK.LBrace) { depth++; tok.Next(); continue; }
            if (t.Kind == TK.RBrace) { depth--; tok.Next(); continue; }

            // Only process annotations at the top level of the class body
            if (t.Kind != TK.Ident || depth > 1) { tok.Next(); continue; }

            switch (t.Text)
            {
                case "API_FUNCTION":
                    tok.Next();
                    var m = ParseMethod(tok, cls.Name);
                    if (m != null) cls.Methods.Add(m);
                    break;
                case "API_PROPERTY":
                    tok.Next();
                    var p = ParseProperty(tok);
                    if (p != null) cls.Properties.Add(p);
                    break;
                case "API_FIELD":
                    tok.Next();
                    var f = ParseField(tok);
                    if (f != null) cls.Fields.Add(f);
                    break;
                default:
                    tok.Next();
                    break;
            }
        }
    }

    // ── API_STRUCT ────────────────────────────────────────────────────────────

    private void ParseStruct(Tokenizer tok, FileInfo file)
    {
        ConsumeAnnotationArgs(tok);
        if (tok.Peek().Kind != TK.Ident || tok.Peek().Text != "struct") return;
        tok.Next();
        if (tok.Peek().Kind != TK.Ident) return;
        var name = tok.Next().Text;

        var st = new StructInfo { Name = name, Namespace = _namespace, NativeModule = _moduleName };

        while (tok.Peek().Kind != TK.LBrace && tok.Peek().Kind != TK.EOF) tok.Next();
        if (tok.Peek().Kind == TK.EOF) return;
        tok.Next(); // {

        while (tok.Peek().Kind != TK.RBrace && tok.Peek().Kind != TK.EOF)
        {
            if (tok.Peek().Kind == TK.Ident && tok.Peek().Text == "API_FIELD")
            {
                tok.Next();
                var f = ParseField(tok);
                if (f != null) st.Fields.Add(f);
            }
            else tok.Next();
        }
        if (tok.Peek().Kind == TK.RBrace) tok.Next();

        file.Structs.Add(st);
    }

    // ── API_ENUM ──────────────────────────────────────────────────────────────

    private void ParseEnum(Tokenizer tok, FileInfo file)
    {
        ConsumeAnnotationArgs(tok);
        if (tok.Peek().Kind != TK.Ident || tok.Peek().Text != "enum") return;
        tok.Next();

        // enum class or plain enum
        if (tok.Peek().Kind == TK.Ident && tok.Peek().Text == "class") tok.Next();

        if (tok.Peek().Kind != TK.Ident) return;
        var name = tok.Next().Text;

        // skip optional : underlying_type
        if (tok.Peek().Kind == TK.Colon) { tok.Next(); tok.Next(); }

        if (tok.Peek().Kind != TK.LBrace) return;
        tok.Next(); // {

        var enm = new EnumInfo { Name = name, Namespace = _namespace, NativeModule = _moduleName };
        int cur = 0;

        while (tok.Peek().Kind != TK.RBrace && tok.Peek().Kind != TK.EOF)
        {
            if (tok.Peek().Kind != TK.Ident) { tok.Next(); continue; }
            var vname = tok.Next().Text;

            if (tok.Peek().Kind == TK.Eq)
            {
                tok.Next(); // =
                var num = new StringBuilder();
                while (tok.Peek().Kind != TK.Comma && tok.Peek().Kind != TK.RBrace && tok.Peek().Kind != TK.EOF)
                    num.Append(tok.Next().Text);
                if (int.TryParse(num.ToString().Trim(), out int v)) cur = v;
            }

            enm.Values.Add((vname, cur++));
            if (tok.Peek().Kind == TK.Comma) tok.Next();
        }
        if (tok.Peek().Kind == TK.RBrace) tok.Next();

        file.Enums.Add(enm);
    }

    // ── API_INTERFACE ─────────────────────────────────────────────────────────

    private void ParseInterface(Tokenizer tok, FileInfo file)
    {
        ConsumeAnnotationArgs(tok);
        if (tok.Peek().Kind != TK.Ident) return;
        tok.Next(); // class / struct
        if (tok.Peek().Kind != TK.Ident) return;
        var name = tok.Next().Text;

        var iface = new InterfaceInfo { Name = name, Namespace = _namespace, NativeModule = _moduleName };

        while (tok.Peek().Kind != TK.LBrace && tok.Peek().Kind != TK.EOF) tok.Next();
        if (tok.Peek().Kind == TK.EOF) return;
        tok.Next(); // {

        while (tok.Peek().Kind != TK.RBrace && tok.Peek().Kind != TK.EOF)
        {
            if (tok.Peek().Kind == TK.Ident && tok.Peek().Text == "API_FUNCTION")
            {
                tok.Next();
                var m = ParseMethod(tok, name);
                if (m != null) iface.Methods.Add(m);
            }
            else tok.Next();
        }
        if (tok.Peek().Kind == TK.RBrace) tok.Next();

        file.Interfaces.Add(iface);
    }

    // ── API_EXPORT (free function) ────────────────────────────────────────────

    private void ParseFreeFunction(Tokenizer tok, FileInfo file)
    {
        ConsumeAnnotationArgs(tok); // ()

        var parts = CollectTypeAndName(tok);
        if (parts.Count < 2) return;

        var fn = new FreeFunctionInfo
        {
            Name       = parts[^1],
            ReturnType = string.Join(" ", parts[..^1]).Trim(),
        };

        if (tok.Peek().Kind != TK.LParen) return;
        tok.Next(); // (
        fn.Parameters = ParseParameters(tok);
        SkipToSemiOrBrace(tok);

        file.FreeFunctions.Add(fn);
    }

    // ── API_FUNCTION (method) ─────────────────────────────────────────────────

    private FunctionInfo? ParseMethod(Tokenizer tok, string parentName)
    {
        var attrs = ConsumeAnnotationArgs(tok);
        var m = new FunctionInfo { ParentName = parentName, Attributes = attrs };

        // Collect modifiers + return-type + name tokens until `(`
        var parts = new List<string>();
        while (tok.Peek().Kind != TK.LParen && tok.Peek().Kind != TK.EOF)
        {
            var t = tok.Next();
            switch (t.Kind)
            {
                case TK.Ident when t.Text is "virtual":                    m.IsVirtual = true; break;
                case TK.Ident when t.Text is "static":                     m.IsStatic  = true; break;
                case TK.Ident when t.Text is "inline" or "FORCE_INLINE"
                                           or "explicit" or "constexpr":   break;
                case TK.Ident:
                    parts.Add(tok.Peek().Kind == TK.Lt ? t.Text + ConsumeAngleBlock(tok) : t.Text);
                    break;
                case TK.Star:
                    if (parts.Count > 0) parts[^1] += "*"; else parts.Add("*");
                    break;
                case TK.Amp:
                    if (parts.Count > 0) parts[^1] += "&"; else parts.Add("&");
                    break;
                case TK.Tilde:
                    // destructor prefix — prepend to next token
                    if (tok.Peek().Kind == TK.Ident) parts.Add("~" + tok.Next().Text);
                    break;
            }
        }

        if (parts.Count == 0 || tok.Peek().Kind != TK.LParen) return null;

        m.Name       = parts[^1];
        m.ReturnType = parts.Count > 1 ? string.Join(" ", parts[..^1]).Trim() : "void";
        if (string.IsNullOrWhiteSpace(m.ReturnType)) m.ReturnType = "void";

        tok.Next(); // (
        m.Parameters = ParseParameters(tok);
        SkipFunctionTail(tok);

        m.Glue = new FunctionInfo.GlueInfo { LibraryEntryPoint = $"{parentName}_{m.Name}" };
        return m;
    }

    // ── API_PROPERTY ──────────────────────────────────────────────────────────

    private PropertyInfo? ParseProperty(Tokenizer tok)
    {
        ConsumeAnnotationArgs(tok);
        var parts = CollectTypeAndName(tok);
        if (parts.Count < 2) return null;

        var raw = parts[^1];
        // Strip Get/Set prefix to derive the canonical property name
        var propName = (raw.StartsWith("Get", StringComparison.Ordinal) ||
                        raw.StartsWith("Set", StringComparison.Ordinal))
                     ? raw[3..] : raw;

        // skip parameter list
        if (tok.Peek().Kind == TK.LParen)
        {
            tok.Next();
            while (tok.Peek().Kind != TK.RParen && tok.Peek().Kind != TK.EOF) tok.Next();
            if (tok.Peek().Kind == TK.RParen) tok.Next();
        }
        SkipFunctionTail(tok);

        return new PropertyInfo
        {
            Name      = propName,
            Type      = string.Join(" ", parts[..^1]).Trim(),
            HasGetter = true,
            HasSetter = true,
        };
    }

    // ── API_FIELD ─────────────────────────────────────────────────────────────

    private FieldInfo? ParseField(Tokenizer tok)
    {
        ConsumeAnnotationArgs(tok);
        var parts = CollectTypeAndName(tok);
        if (parts.Count < 2) return null;

        while (tok.Peek().Kind != TK.Semi && tok.Peek().Kind != TK.EOF) tok.Next();
        if (tok.Peek().Kind == TK.Semi) tok.Next();

        return new FieldInfo
        {
            Name = parts[^1],
            Type = string.Join(" ", parts[..^1]).Trim(),
        };
    }

    // ── Parameter list ────────────────────────────────────────────────────────

    private List<ParameterInfo> ParseParameters(Tokenizer tok)
    {
        var result = new List<ParameterInfo>();
        if (tok.Peek().Kind == TK.RParen) { tok.Next(); return result; }

        while (tok.Peek().Kind != TK.RParen && tok.Peek().Kind != TK.EOF)
        {
            var p = ParseOneParam(tok);
            if (p != null) result.Add(p);
            if (tok.Peek().Kind == TK.Comma) tok.Next();
        }
        if (tok.Peek().Kind == TK.RParen) tok.Next();

        return result;
    }

    private ParameterInfo? ParseOneParam(Tokenizer tok)
    {
        var dir = ParamKind.In;

        // API_PARAM(Out) / API_PARAM(Ref)
        if (tok.Peek().Kind == TK.Ident && tok.Peek().Text == "API_PARAM")
        {
            tok.Next();
            if (tok.Peek().Kind == TK.LParen) tok.Next();
            if (tok.Peek().Kind == TK.Ident)
            {
                var k = tok.Next().Text;
                dir = k.Equals("Out", StringComparison.OrdinalIgnoreCase) ? ParamKind.Out
                    : k.Equals("Ref", StringComparison.OrdinalIgnoreCase) ? ParamKind.Ref
                    : ParamKind.In;
            }
            if (tok.Peek().Kind == TK.RParen) tok.Next();
        }

        var parts  = new List<string>();
        var defVal = "";
        var hasDef = false;

        while (true)
        {
            var t = tok.Peek();
            if (t.Kind is TK.Comma or TK.RParen or TK.EOF) break;
            t = tok.Next();

            if (t.Kind == TK.Eq)
            {
                hasDef = true;
                var dv = new StringBuilder();
                while (tok.Peek().Kind is not (TK.Comma or TK.RParen or TK.EOF))
                    dv.Append(tok.Next().Text);
                defVal = dv.ToString().Trim();
                break;
            }

            switch (t.Kind)
            {
                case TK.Ident:
                    parts.Add(tok.Peek().Kind == TK.Lt ? t.Text + ConsumeAngleBlock(tok) : t.Text);
                    break;
                case TK.Star:
                    if (parts.Count > 0) parts[^1] += "*"; else parts.Add("*");
                    break;
                case TK.Amp:
                    if (parts.Count > 0) parts[^1] += "&"; else parts.Add("&");
                    break;
            }
        }

        if (parts.Count == 0) return null;

        // "void" alone → empty parameter list sentinel
        if (parts.Count == 1 && parts[0] == "void") return null;

        var name = parts.Count > 1 ? parts[^1] : "p";
        var type = parts.Count > 1 ? string.Join(" ", parts[..^1]).Trim() : parts[0];

        return new ParameterInfo
        {
            Name         = name,
            Type         = type,
            Direction    = dir,
            HasDefault   = hasDef,
            DefaultValue = defVal,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Consume `(...)` annotation argument list; return attribute strings split by comma.
    private static List<string> ConsumeAnnotationArgs(Tokenizer tok)
    {
        if (tok.Peek().Kind != TK.LParen) return new List<string>();
        tok.Next(); // (
        var attrs = new List<string>();
        var cur   = new StringBuilder();
        int depth = 1;

        while (depth > 0 && tok.Peek().Kind != TK.EOF)
        {
            var t = tok.Next();
            if      (t.Kind == TK.LParen)                              { depth++; cur.Append('('); }
            else if (t.Kind == TK.RParen && --depth > 0)               cur.Append(')');
            else if (t.Kind == TK.RParen)                              { /* closing paren */ }
            else if (t.Kind == TK.Comma  && depth == 1)                { var s = cur.ToString().Trim(); if (s.Length > 0) attrs.Add(s); cur.Clear(); }
            else                                                        cur.Append(t.Text);
        }

        var last = cur.ToString().Trim();
        if (last.Length > 0) attrs.Add(last);
        return attrs;
    }

    /// Collect identifier tokens (and pointer/reference suffixes) up to `(` or `;`.
    /// Pointer `*` is appended to the preceding identifier, keeping types like "void*" atomic.
    private static List<string> CollectTypeAndName(Tokenizer tok)
    {
        var parts = new List<string>();
        while (tok.Peek().Kind is not (TK.LParen or TK.Semi or TK.EOF))
        {
            var t = tok.Next();
            switch (t.Kind)
            {
                case TK.Ident when t.Text is "virtual" or "static" or "inline"
                                          or "FORCE_INLINE" or "explicit" or "constexpr":
                    break;
                case TK.Ident:
                    parts.Add(tok.Peek().Kind == TK.Lt ? t.Text + ConsumeAngleBlock(tok) : t.Text);
                    break;
                case TK.Star:
                    if (parts.Count > 0) parts[^1] += "*"; else parts.Add("*");
                    break;
                case TK.Amp:
                    if (parts.Count > 0) parts[^1] += "&"; else parts.Add("&");
                    break;
            }
        }
        return parts;
    }

    /// Consume `<...>` template parameter block, returning the full text including `<>`.
    private static string ConsumeAngleBlock(Tokenizer tok)
    {
        if (tok.Peek().Kind != TK.Lt) return "";
        tok.Next();
        var sb    = new StringBuilder("<");
        int depth = 1;
        while (depth > 0 && tok.Peek().Kind != TK.EOF)
        {
            var t = tok.Next();
            sb.Append(t.Text);
            if (t.Kind == TK.Lt) depth++;
            else if (t.Kind == TK.Gt) depth--;
        }
        return sb.ToString();
    }

    /// Skip `public` / `private` / `protected` / `virtual` access specifiers.
    private static void SkipAccessSpecifiers(Tokenizer tok)
    {
        while (tok.Peek().Kind == TK.Ident &&
               tok.Peek().Text is "public" or "private" or "protected" or "virtual")
        {
            tok.Next();
            if (tok.Peek().Kind == TK.Comma) tok.Next();
        }
    }

    /// Skip until `;` or inline `{...}` body — whichever comes first.
    private static void SkipFunctionTail(Tokenizer tok)
    {
        while (tok.Peek().Kind != TK.EOF)
        {
            var t = tok.Peek();
            if (t.Kind == TK.Semi)   { tok.Next(); return; }
            if (t.Kind == TK.LBrace) { SkipBraceBlock(tok); return; }
            if (t.Kind == TK.Eq)     { tok.Next(); tok.Next(); continue; } // = 0 / = delete
            tok.Next();
        }
    }

    private static void SkipBraceBlock(Tokenizer tok)
    {
        if (tok.Peek().Kind != TK.LBrace) return;
        tok.Next();
        int depth = 1;
        while (depth > 0 && tok.Peek().Kind != TK.EOF)
        {
            var t = tok.Next();
            if (t.Kind == TK.LBrace) depth++;
            else if (t.Kind == TK.RBrace) depth--;
        }
    }

    private static void SkipToSemiOrBrace(Tokenizer tok)
    {
        while (tok.Peek().Kind != TK.EOF)
        {
            if (tok.Peek().Kind == TK.Semi)   { tok.Next(); return; }
            if (tok.Peek().Kind == TK.LBrace) { SkipBraceBlock(tok); return; }
            tok.Next();
        }
    }

    private static bool IsVisibilityMacro(string name) =>
        name.EndsWith("_API", StringComparison.Ordinal) ||
        name.EndsWith("_EXPORT", StringComparison.Ordinal) ||
        name.EndsWith("_IMPORT", StringComparison.Ordinal);
}
