using Builder.Declaration;
using Builder.Diagnostics;
using SyntaxTree;
using TypeModel.Enums;
using TypeModel.Symbols;
using TypeModel.Types;
using Builder.Verification;

namespace RazorForge.Checks;

/// <summary>
/// Build-time token lifetimes (RazorForge). A single-thread access token (<c>Viewing</c>/<c>Modifying</c>) remembers
/// the object it was taken from, its SOURCE: <c>c</c> for <c>c.view()</c>, <c>h.inner</c> for <c>h.inner.view()</c>
/// or for a routine on <c>h</c> that returns <c>me.inner.view()</c>, <c>boxes[]</c> (some element) for
/// <c>var b = boxes[0]</c>. The source is free to change: replacing it (<c>h.inner = …</c>), moving it out
/// (<c>steal h</c>), leaving its scope, or calling a routine that can replace it (one whose body assigns that field,
/// directly or through its own calls) ends the token. Only a USE of an ended token is an error (RF-S643), reported
/// where it happens and pointing at what ended it, so nothing is rejected that could not reach freed memory.
/// <para>Each routine gets a REPLACE EFFECT: for each parameter (and <c>me</c>), the parts of it the routine may
/// replace (<c>.inner</c>, <c>.inner.x</c>, or <c>.*</c> for anything inside). A stdlib routine counts through its
/// <c>@readonly</c> marker (anything else may change what is inside its receiver and its entity parameters). A
/// routine that returns a token gets a RETURN ORIGIN: the parameter it takes the token from and the part of it.
/// Every return must take the token from the same parameter (RF-S600), so a caller knows which object it points
/// at. Both summaries are solved as a fixpoint over all user routines.</para>
/// <para>Within a routine the check follows the control flow: a token ended on one branch is ended after the
/// branches meet, and a loop is walked until what it ends no longer changes. A call that can replace the source of
/// a token passed to that same call is rejected at the call (RF-S639).</para>
/// </summary>
internal sealed class TokenLifetimeChecker(DiagnosticReporter report) : IUserBodyCheck
{
    private const string MeSlot = "me";

    /// <summary>The origin of a token whose source the builder cannot tell (a routine value, an unwrapped
    /// variant): any change ends it.</summary>
    private const string UnknownOrigin = "**";

    /// <summary>The suffix for "somewhere inside": <c>h.*</c> is any part of <c>h</c> below it.</summary>
    private const string Inside = ".*";

    private readonly Dictionary<string, RoutineDeclaration> _userRoutineDeclarations =
        new(comparer: StringComparer.Ordinal);

    private readonly Dictionary<RoutineDeclaration, Dictionary<string, HashSet<string>>> _replaceEffects =
        new(comparer: ReferenceEqualityComparer.Instance);

    private readonly Dictionary<RoutineDeclaration, (string Slot, string Suffix)?> _returnOrigins =
        new(comparer: ReferenceEqualityComparer.Instance);

    private readonly HashSet<RoutineDeclaration> _checked = new(comparer: ReferenceEqualityComparer.Instance);

    /// <summary>Runs the token-lifetime checks over the user programs.</summary>
    public void Check(IReadOnlyList<Program> programs)
    {
        List<RoutineDeclaration> routines = programs.SelectMany(selector: EnumerateRoutines)
                                                    .ToList();
        foreach (RoutineDeclaration routine in routines)
        {
            if (routine.ResolvedInfo is { } info)
            {
                _userRoutineDeclarations.TryAdd(key: info.RegistryKey, value: routine);
            }
        }

        ComputeSummaries();

        foreach (RoutineDeclaration routine in routines)
        {
            if (_checked.Add(item: routine))
            {
                CheckReturns(routine: routine);
                new Flow(owner: this, routine: routine).Run();
            }
        }
    }

    private static IEnumerable<RoutineDeclaration> EnumerateRoutines(Program program)
    {
        foreach (ISyntaxTreeNode node in program.Declarations)
        {
            switch (node)
            {
                case RoutineDeclaration r:
                    yield return r;
                    break;
                case EntityDeclaration e:
                    foreach (RoutineDeclaration m in e.Members.OfType<RoutineDeclaration>())
                    {
                        yield return m;
                    }

                    break;
                case RecordDeclaration rec:
                    foreach (RoutineDeclaration m in rec.Members.OfType<RoutineDeclaration>())
                    {
                        yield return m;
                    }

                    break;
                case CrashableDeclaration cr:
                    foreach (RoutineDeclaration m in cr.Members.OfType<RoutineDeclaration>())
                    {
                        yield return m;
                    }

                    break;
            }
        }
    }

    // ---- Paths ----------------------------------------------------------------------------------------

    /// <summary>Whether a token type this check follows: the single-thread tokens. A lock token is opened with
    /// <c>using</c> and released at its end.</summary>
    internal static bool IsTrackedToken(TypeSymbol? type)
    {
        return type?.BareName is RuntimeContract.Viewing or RuntimeContract.Modifying;
    }

    private static bool IsDeep(string path)
    {
        return path.EndsWith(value: Inside, comparisonType: StringComparison.Ordinal);
    }

    private static string DeepBase(string path)
    {
        return path[..^Inside.Length];
    }

    private static bool UnderOrEqual(string path, string prefix)
    {
        return AccessPaths.IsPrefixOrEqual(prefix: prefix, path: path);
    }

    private static bool StrictlyUnder(string path, string prefix)
    {
        return path != prefix && UnderOrEqual(path: path, prefix: prefix);
    }

    /// <summary>Whether replacing <paramref name="written"/> (a part, or <c>p.*</c> for anything inside
    /// <c>p</c>) ends a token whose source is <paramref name="origin"/>.</summary>
    private static bool Ends(string written, string origin)
    {
        if (origin == UnknownOrigin)
        {
            return !written.StartsWith(value: "~", comparisonType: StringComparison.Ordinal);
        }

        if (origin.StartsWith(value: "~", comparisonType: StringComparison.Ordinal) ||
            written.StartsWith(value: "~", comparisonType: StringComparison.Ordinal))
        {
            return origin == written;
        }

        bool wDeep = IsDeep(path: written);
        bool oDeep = IsDeep(path: origin);
        string w = wDeep ? DeepBase(path: written) : written;
        string o = oDeep ? DeepBase(path: origin) : origin;
        return (wDeep, oDeep) switch
        {
            (false, false) => UnderOrEqual(path: o, prefix: w),
            (false, true) => UnderOrEqual(path: o, prefix: w) || StrictlyUnder(path: w, prefix: o),
            (true, false) => StrictlyUnder(path: o, prefix: w),
            (true, true) => UnderOrEqual(path: o, prefix: w) || UnderOrEqual(path: w, prefix: o)
        };
    }

    private static string RootName(string path)
    {
        int end = path.IndexOfAny(anyOf: ['.', '[']);
        return end < 0
            ? path
            : path[..end];
    }

    /// <summary>The part of <paramref name="path"/> below its root, with its leading separator.</summary>
    private static string Suffix(string path)
    {
        return path[RootName(path: path).Length..];
    }

    /// <summary>A path joined with a part suffix: <c>h</c> + <c>.inner</c>, <c>h.a</c> + <c>.*</c>.</summary>
    private static string Join(string path, string suffix)
    {
        if (suffix.Length == 0)
        {
            return path;
        }

        return IsDeep(path: path)
            ? path
            : path + suffix;
    }

    /// <summary>How a path reads in a message: an element (<c>boxes[]</c>) reads <c>boxes[...]</c>, and the
    /// inside of <c>h</c> (<c>h.*</c>) reads <c>h</c>.</summary>
    private static string Shown(string path)
    {
        string p = IsDeep(path: path)
            ? DeepBase(path: path)
            : path;
        return p.Replace(oldValue: "[]", newValue: "[...]", comparisonType: StringComparison.Ordinal);
    }

    private static Expression Unwrap(Expression expr)
    {
        return expr is NamedArgumentExpression named
            ? named.Value
            : expr;
    }

    // ---- Callee summaries -------------------------------------------------------------------------------

    private RoutineDeclaration? FindUserDeclaration(RoutineInfo routine)
    {
        if (_userRoutineDeclarations.TryGetValue(key: routine.RegistryKey, value: out RoutineDeclaration? d))
        {
            return d;
        }

        return routine.GenericDefinition is { } def &&
               _userRoutineDeclarations.TryGetValue(key: def.RegistryKey, value: out RoutineDeclaration? g)
            ? g
            : null;
    }

    /// <summary>The parts of each slot (parameter name, or <c>me</c>) a call to <paramref name="callee"/> may
    /// replace.</summary>
    private Dictionary<string, HashSet<string>> CalleeEffects(RoutineInfo callee)
    {
        if (FindUserDeclaration(routine: callee) is { } decl &&
            _replaceEffects.TryGetValue(key: decl, value: out Dictionary<string, HashSet<string>>? known))
        {
            return known;
        }

        var effects = new Dictionary<string, HashSet<string>>(comparer: StringComparer.Ordinal);
        if (callee.IsReadOnly || IsTrackedToken(type: callee.ReturnType))
        {
            return effects;
        }

        if (callee.OwnerType != null && callee.Kind is not (RoutineKind.CommonRoutine or RoutineKind.Creator))
        {
            effects[key: MeSlot] = [Inside];
        }

        foreach (ParamInfo p in callee.Parameters)
        {
            if (p.Type is EntityTypeSymbol || p.Type.BareName == RuntimeContract.Modifying)
            {
                effects[key: p.Name] = [Inside];
            }
        }

        return effects;
    }

    /// <summary>The slot a call to <paramref name="callee"/> takes its returned token from, and the part of it.
    /// Null when the callee returns no token.</summary>
    private (string Slot, string Suffix)? CalleeReturnOrigin(RoutineInfo callee)
    {
        if (!IsTrackedToken(type: callee.ReturnType))
        {
            return null;
        }

        if (FindUserDeclaration(routine: callee) is { } decl &&
            _returnOrigins.TryGetValue(key: decl, value: out (string, string)? known))
        {
            return known ?? (MeSlot, Inside);
        }

        return callee.Name switch
        {
            "view" or "modify" => (MeSlot, ""),
            "view_at" or "modify_at" => (MeSlot, "[]"),
            _ when callee.OwnerType != null => (MeSlot, Inside),
            _ => null
        };
    }

    /// <summary>The argument of <paramref name="call"/> bound to each parameter of <paramref name="callee"/>.</summary>
    private static IEnumerable<(string Param, Expression Arg)> BoundArguments(CallExpression call,
        RoutineInfo callee)
    {
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            Expression arg = call.Arguments[index: i];
            string? paramName = arg is NamedArgumentExpression named
                ? named.Name
                : i < callee.Parameters.Count
                    ? callee.Parameters[index: i].Name
                    : null;
            if (paramName != null)
            {
                yield return (paramName, Unwrap(expr: arg));
            }
        }
    }

    private static bool IsRoutineValueCall(CallExpression call)
    {
        return call.Callee.ResolvedType is RoutineTypeSymbol;
    }

    private static bool TakesReceiver(RoutineInfo callee)
    {
        return callee.Kind is not (RoutineKind.CommonRoutine or RoutineKind.Creator);
    }

    // ---- Origins ----------------------------------------------------------------------------------------

    /// <summary>
    /// The paths an operand stands for: a name or field chain (<c>h.inner</c>), an element (<c>boxes[]</c>), a token
    /// variable's sources, or a token a call hands out. Null for a temporary.
    /// </summary>
    private HashSet<string>? PathsOf(Expression expr, Func<string, HashSet<string>?> tokenOrigins)
    {
        switch (Unwrap(expr: expr))
        {
            case IdentifierExpression id:
                return tokenOrigins(arg: id.Name) is { } held
                    ? held
                    : [id.Name];
            case MemberExpression { Object: var inner, MemberName: var field }:
                return PathsOf(expr: inner, tokenOrigins: tokenOrigins) is { } owners
                    ? owners.Select(selector: o => Join(path: o, suffix: "." + field))
                            .ToHashSet()
                    : null;
            case IndexExpression { Object: var container }:
                return PathsOf(expr: container, tokenOrigins: tokenOrigins) is { } containers
                    ? containers.Select(selector: c => Join(path: c, suffix: "[]"))
                                .ToHashSet()
                    : null;
            case CallExpression call when IsTrackedToken(type: call.ResolvedType):
                return OriginsOf(expr: call, tokenOrigins: tokenOrigins);
            case CallExpression { Callee: MemberExpression { Object: var receiver, MemberName: var verb } }
                when verb == RuntimeContract.Access || verb == RuntimeContract.Control:
                return PathsOf(expr: receiver, tokenOrigins: tokenOrigins);
            default:
                return null;
        }
    }

    /// <summary>
    /// The sources of a token-valued expression. Null for a temporary (the caller gives it the statement's
    /// temporary origin); <see cref="UnknownOrigin"/> when the builder cannot tell.
    /// </summary>
    private HashSet<string>? OriginsOf(Expression expr, Func<string, HashSet<string>?> tokenOrigins)
    {
        switch (Unwrap(expr: expr))
        {
            case IdentifierExpression id:
                return tokenOrigins(arg: id.Name) ?? [id.Name];
            case IndexExpression { ReadsElementToken: true, Object: var container }:
                return PathsOf(expr: container, tokenOrigins: tokenOrigins) is { } containers
                    ? containers.Select(selector: c => Join(path: c, suffix: "[]"))
                                .ToHashSet()
                    : null;
            case CallExpression { ResolvedRoutine: { } callee } call when !IsRoutineValueCall(call: call) &&
                                                                         CalleeReturnOrigin(callee: callee) is
                                                                             { } from:
            {
                Expression? source = from.Slot == MeSlot
                    ? (call.Callee as MemberExpression)?.Object
                    : BoundArguments(call: call, callee: callee)
                     .Where(predicate: b => b.Param == from.Slot)
                     .Select(selector: b => b.Arg)
                     .FirstOrDefault();
                if (source == null)
                {
                    return [UnknownOrigin];
                }

                return PathsOf(expr: source, tokenOrigins: tokenOrigins) is { } bases
                    ? bases.Select(selector: b => Join(path: b, suffix: from.Suffix))
                           .ToHashSet()
                    : null;
            }
            case ConditionalExpression cond:
            {
                HashSet<string>? a = OriginsOf(expr: cond.TrueExpression, tokenOrigins: tokenOrigins);
                HashSet<string>? b = OriginsOf(expr: cond.FalseExpression, tokenOrigins: tokenOrigins);
                return a == null || b == null
                    ? null
                    : a.Union(second: b)
                       .ToHashSet();
            }
            default:
                return [UnknownOrigin];
        }
    }

    // ---- Summaries --------------------------------------------------------------------------------------

    private void ComputeSummaries()
    {
        foreach (RoutineDeclaration decl in _userRoutineDeclarations.Values)
        {
            _replaceEffects.TryAdd(key: decl,
                value: new Dictionary<string, HashSet<string>>(comparer: StringComparer.Ordinal));
            _returnOrigins.TryAdd(key: decl, value: null);
        }

        bool changed = true;
        int rounds = 0;
        while (changed && rounds++ < 50)
        {
            changed = false;
            foreach (RoutineDeclaration decl in _userRoutineDeclarations.Values)
            {
                Dictionary<string, HashSet<string>> effects = _replaceEffects[key: decl];
                (Dictionary<string, HashSet<string>> found, (string, string)? ret) = DirectSummary(routine: decl);
                foreach ((string slot, HashSet<string> parts) in found)
                {
                    if (!effects.TryGetValue(key: slot, value: out HashSet<string>? known))
                    {
                        known = new HashSet<string>(comparer: StringComparer.Ordinal);
                        effects[key: slot] = known;
                    }

                    foreach (string part in parts)
                    {
                        changed |= known.Add(item: part);
                    }
                }

                if (!Equals(objA: _returnOrigins[key: decl], objB: ret))
                {
                    _returnOrigins[key: decl] = ret;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// The slots a returned token may come from: <c>me</c>, and the parameters the caller keeps owning (a token
    /// parameter, or one bound by <c>Accessing</c>/<c>Controlling</c>). An entity parameter is owned by the routine
    /// and freed when it returns, so a token taken from it cannot be handed back.
    /// </summary>
    private static HashSet<string> ReturnSlots(RoutineDeclaration routine)
    {
        var slots = new HashSet<string>(comparer: StringComparer.Ordinal) { MeSlot };
        foreach (ParamInfo p in routine.ResolvedInfo?.Parameters ?? [])
        {
            if (IsTrackedToken(type: p.Type) || p.Type is GenericParameterTypeSymbol)
            {
                slots.Add(item: p.Name);
            }
        }

        return slots;
    }

    /// <summary>The slots a routine has (its parameters and <c>me</c>).</summary>
    private static HashSet<string> Slots(RoutineDeclaration routine)
    {
        return routine.Parameters.Select(selector: p => p.Name)
                      .Append(element: MeSlot)
                      .ToHashSet(comparer: StringComparer.Ordinal);
    }

    /// <summary>The token variables of a routine with every source they are ever given, in slot terms.</summary>
    private Dictionary<string, HashSet<string>> FlowInsensitiveTokenOrigins(RoutineDeclaration routine)
    {
        var origins = new Dictionary<string, HashSet<string>>(comparer: StringComparer.Ordinal);

        HashSet<string>? Lookup(string name)
        {
            return origins.GetValueOrDefault(key: name);
        }

        void Add(string name, Expression? value)
        {
            if (!origins.TryGetValue(key: name, value: out HashSet<string>? set))
            {
                set = new HashSet<string>(comparer: StringComparer.Ordinal);
                origins[key: name] = set;
            }

            if (value != null && OriginsOf(expr: value, tokenOrigins: Lookup) is { } from)
            {
                set.UnionWith(other: from);
            }
            else
            {
                set.Add(item: "~local");
            }
        }

        // Two passes so a token variable copied from one declared later in the text still gets its sources.
        for (int pass = 0; pass < 2; pass++)
        {
            AstWalker.Walk(root: routine.Body,
                visit: node =>
                {
                    switch (node)
                    {
                        case VariableDeclaration { Initializer: { } init } v when IsTrackedToken(type: init.ResolvedType):
                            Add(name: v.Name, value: init);
                            break;
                        case UsingStatement u when IsTrackedToken(type: u.Resource.ResolvedType):
                            Add(name: u.Name, value: u.Resource);
                            break;
                        case AssignmentStatement { Target: IdentifierExpression t, Value: var value }
                            when IsTrackedToken(type: value.ResolvedType):
                            Add(name: t.Name, value: value);
                            break;
                    }
                });
        }

        return origins;
    }

    /// <summary>The replace effect and the return origin <paramref name="routine"/>'s body gives itself, given
    /// what is known of its callees so far.</summary>
    private (Dictionary<string, HashSet<string>> Effects, (string, string)? Return) DirectSummary(
        RoutineDeclaration routine)
    {
        HashSet<string> slots = Slots(routine: routine);
        Dictionary<string, HashSet<string>> tokenVars = FlowInsensitiveTokenOrigins(routine: routine);
        var effects = new Dictionary<string, HashSet<string>>(comparer: StringComparer.Ordinal);

        HashSet<string>? Lookup(string name)
        {
            return tokenVars.GetValueOrDefault(key: name);
        }

        void Record(string path)
        {
            string root = RootName(path: path);
            string suffix = Suffix(path: path);
            if (!slots.Contains(item: root) || suffix.Length == 0)
            {
                return;
            }

            if (!effects.TryGetValue(key: root, value: out HashSet<string>? set))
            {
                set = new HashSet<string>(comparer: StringComparer.Ordinal);
                effects[key: root] = set;
            }

            set.Add(item: suffix);
        }

        void RecordWrite(Expression target)
        {
            if (target is IdentifierExpression id && tokenVars.ContainsKey(key: id.Name))
            {
                return;
            }

            if (PathsOf(expr: target, tokenOrigins: Lookup) is { } written)
            {
                foreach (string w in written)
                {
                    Record(path: w);
                }
            }
        }

        AstWalker.Walk(root: routine.Body,
            visit: node =>
            {
                switch (node)
                {
                    case AssignmentStatement assign:
                        RecordWrite(target: assign.Target);
                        break;
                    case BinaryExpression { Operator: BinaryOperator.Assign, Left: var left }:
                        RecordWrite(target: left);
                        break;
                    case StealExpression steal:
                        RecordWrite(target: steal.Operand);
                        break;
                    case CallExpression call:
                        foreach (string written in CallWrites(call: call, tokenOrigins: Lookup)
                                    .Select(selector: w => w.Path))
                        {
                            Record(path: written);
                        }

                        break;
                }
            });

        (string, string)? ret = null;
        if (IsTrackedToken(type: routine.ResolvedInfo?.ReturnType))
        {
            ret = ReturnSummary(routine: routine, slots: ReturnSlots(routine: routine), lookup: Lookup).Origin;
        }

        return (effects, ret);
    }

    /// <summary>The parts a call may replace, as absolute paths, each with the name of the routine doing it.</summary>
    private List<(string Path, string Callee)> CallWrites(CallExpression call,
        Func<string, HashSet<string>?> tokenOrigins)
    {
        var writes = new List<(string, string)>();
        if (IsRoutineValueCall(call: call))
        {
            foreach (Expression arg in call.Arguments)
            {
                foreach (string p in PathsOf(expr: arg, tokenOrigins: tokenOrigins) ?? [])
                {
                    writes.Add(item: (Join(path: p, suffix: Inside), ""));
                }
            }

            return writes;
        }

        if (call.ResolvedRoutine is not { } callee)
        {
            return writes;
        }

        Dictionary<string, HashSet<string>> effects = CalleeEffects(callee: callee);
        if (effects.Count == 0)
        {
            return writes;
        }

        if (effects.TryGetValue(key: MeSlot, value: out HashSet<string>? meParts) && TakesReceiver(callee: callee) &&
            call.Callee is MemberExpression { Object: var receiver } &&
            PathsOf(expr: receiver, tokenOrigins: tokenOrigins) is { } receivers)
        {
            foreach (string r in receivers)
            {
                foreach (string part in meParts)
                {
                    writes.Add(item: (Join(path: r, suffix: part), callee.Name));
                }
            }
        }

        foreach ((string param, Expression arg) in BoundArguments(call: call, callee: callee))
        {
            if (!effects.TryGetValue(key: param, value: out HashSet<string>? parts) ||
                PathsOf(expr: arg, tokenOrigins: tokenOrigins) is not { } args)
            {
                continue;
            }

            foreach (string a in args)
            {
                foreach (string part in parts)
                {
                    writes.Add(item: (Join(path: a, suffix: part), callee.Name));
                }
            }
        }

        return writes;
    }

    /// <summary>The return origin of a token-returning routine, and the returns that break the one-source rule.</summary>
    private ((string, string)? Origin, List<(ReturnStatement Ret, string Source)> Returns) ReturnSummary(
        RoutineDeclaration routine, HashSet<string> slots, Func<string, HashSet<string>?> lookup)
    {
        var returns = new List<(ReturnStatement, string)>();
        AstWalker.Walk(root: routine.Body,
            visit: node =>
            {
                if (node is ReturnStatement { Value: { } value } ret)
                {
                    HashSet<string> from = OriginsOf(expr: value, tokenOrigins: lookup) ?? ["~"];
                    foreach (string f in from)
                    {
                        returns.Add(item: (ret, f));
                    }
                }
            });

        List<(ReturnStatement Ret, string Source)> fromSlots = returns
                                                               .Where(predicate: r => slots.Contains(item: RootName(path: r.Item2)))
                                                               .ToList();
        if (fromSlots.Count == 0 || fromSlots.Count != returns.Count ||
            fromSlots.Select(selector: r => RootName(path: r.Source))
                     .Distinct()
                     .Count() != 1)
        {
            return (null, returns);
        }

        string slot = RootName(path: fromSlots[index: 0].Source);
        List<string> suffixes = fromSlots.Select(selector: r => Suffix(path: r.Source))
                                         .Distinct()
                                         .ToList();
        string suffix = suffixes.Count == 1
            ? suffixes[index: 0]
            : Inside;
        return ((slot, suffix), returns);
    }

    /// <summary>RF-S600: every return of a token-returning routine takes the token from one and the same
    /// parameter (<c>me</c> included), never from a local or a temporary.</summary>
    private void CheckReturns(RoutineDeclaration routine)
    {
        if (!IsTrackedToken(type: routine.ResolvedInfo?.ReturnType))
        {
            return;
        }

        HashSet<string> slots = ReturnSlots(routine: routine);
        HashSet<string> owned = Slots(routine: routine);
        Dictionary<string, HashSet<string>> tokenVars = FlowInsensitiveTokenOrigins(routine: routine);
        ((string, string)? origin, List<(ReturnStatement Ret, string Source)> returns) =
            ReturnSummary(routine: routine, slots: slots, lookup: name => tokenVars.GetValueOrDefault(key: name));
        if (origin != null)
        {
            return;
        }

        if (returns.FirstOrDefault(predicate: r => !slots.Contains(item: RootName(path: r.Source))) is
            { Ret: not null } local)
        {
            string what = local.Source.StartsWith(value: "~", comparisonType: StringComparison.Ordinal) ||
                          local.Source == UnknownOrigin
                ? "a temporary or something the builder cannot trace to a parameter"
                : owned.Contains(item: RootName(path: local.Source))
                    ? $"'{Shown(path: local.Source)}', a parameter this routine takes ownership of"
                    : $"'{Shown(path: local.Source)}', which belongs to this routine";
            report(code: SemanticDiagnosticCode.TokenReturnNotAllowed,
                message:
                $"You are returning a token taken from {what}. It is gone when the routine returns, so the token " +
                "would point at freed memory. Return a token taken from 'me' or from a token parameter " +
                "(declare it Viewing[T] or Modifying[T]; callers can still pass the entity itself).",
                location: local.Ret.Location);
            return;
        }

        (ReturnStatement firstRet, string firstSource) = returns[index: 0];
        (ReturnStatement otherRet, string otherSource) =
            returns.First(predicate: r => RootName(path: r.Source) != RootName(path: firstSource));
        report(code: SemanticDiagnosticCode.TokenReturnNotAllowed,
            message:
            $"This routine returns a token taken from '{RootName(path: firstSource)}' (line {firstRet.Location.Line}) " +
            $"and one taken from '{RootName(path: otherSource)}' (line {otherRet.Location.Line}). A caller has to know " +
            "which object the token points at, so every return must take it from the same parameter.",
            location: otherRet.Location);
    }

    // ---- Flow -------------------------------------------------------------------------------------------

    /// <summary>A token variable at one point of the routine: where it may come from, and what ended it.</summary>
    private sealed record TokenState(HashSet<string> Origins, string? EndedBy, SourceLocation? EndedAt);

    /// <summary>Token variables at one point; null when the point cannot be reached.</summary>
    private sealed class State(Dictionary<string, TokenState> tokens)
    {
        public Dictionary<string, TokenState> Tokens { get; } = tokens;

        public State Clone()
        {
            return new State(tokens: new Dictionary<string, TokenState>(dictionary: Tokens,
                comparer: StringComparer.Ordinal));
        }

        public static State? Merge(State? a, State? b)
        {
            if (a == null)
            {
                return b?.Clone();
            }

            if (b == null)
            {
                return a.Clone();
            }

            var merged = new Dictionary<string, TokenState>(comparer: StringComparer.Ordinal);
            foreach (string name in a.Tokens.Keys.Union(second: b.Tokens.Keys))
            {
                TokenState? x = a.Tokens.GetValueOrDefault(key: name);
                TokenState? y = b.Tokens.GetValueOrDefault(key: name);
                if (x == null || y == null)
                {
                    merged[key: name] = x ?? y!;
                    continue;
                }

                merged[key: name] = new TokenState(Origins: x.Origins.Union(second: y.Origins)
                                                              .ToHashSet(),
                    EndedBy: x.EndedBy ?? y.EndedBy,
                    EndedAt: x.EndedAt ?? y.EndedAt);
            }

            return new State(tokens: merged);
        }

        public bool SameAs(State? other)
        {
            if (other == null || other.Tokens.Count != Tokens.Count)
            {
                return false;
            }

            foreach ((string name, TokenState t) in Tokens)
            {
                if (!other.Tokens.TryGetValue(key: name, value: out TokenState? o) ||
                    (t.EndedBy == null) != (o.EndedBy == null) || !t.Origins.SetEquals(other: o.Origins))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Walks one routine's body in control-flow order.</summary>
    private sealed class Flow(TokenLifetimeChecker owner, RoutineDeclaration routine)
    {
        private readonly HashSet<string> _reported = new(comparer: StringComparer.Ordinal);

        /// <summary>The iterators of `each` loops over a read token (`each item in v` with `v: Viewing[List[T]]`).</summary>
        private readonly HashSet<string> _readIterators = new(comparer: StringComparer.Ordinal);

        /// <summary>The loop variables those iterators hand out, while their loop body runs: an element seen
        /// through a read token is read-only too.</summary>
        private readonly HashSet<string> _readElements = new(comparer: StringComparer.Ordinal);
        private readonly Stack<(List<State?> Breaks, List<State?> Continues)> _loops = new();
        private readonly Stack<List<string>> _blockLocals = new();
        private State? _state;
        private bool _reporting = true;
        private int _statementIndex;
        private List<string>? _callEnds;

        public void Run()
        {
            var start = new Dictionary<string, TokenState>(comparer: StringComparer.Ordinal);
            if (routine.ResolvedInfo is { } info)
            {
                foreach (ParamInfo p in info.Parameters.Where(predicate: p => IsTrackedToken(type: p.Type)))
                {
                    start[key: p.Name] = new TokenState(Origins: [p.Name], EndedBy: null, EndedAt: null);
                }
            }

            _state = new State(tokens: start);
            Statement(stmt: routine.Body);
        }

        private HashSet<string>? Held(string name)
        {
            return _state?.Tokens.TryGetValue(key: name, value: out TokenState? t) == true
                ? t.Origins
                : null;
        }

        // -- ending tokens --

        private void End(string written, string why, SourceLocation at)
        {
            _callEnds?.Add(item: written);
            if (_state == null)
            {
                return;
            }

            foreach ((string name, TokenState t) in _state.Tokens.ToList())
            {
                if (t.EndedBy == null &&
                    t.Origins.FirstOrDefault(predicate: o => Ends(written: written, origin: o)) is { } ended)
                {
                    string from = ended == UnknownOrigin || ended.StartsWith(value: "~", comparisonType: StringComparison.Ordinal)
                        ? "the object it was taken from"
                        : $"'{Shown(path: ended)}', where it was taken from,";
                    _state.Tokens[key: name] = t with { EndedBy = $"{from} {why}", EndedAt = at };
                }
            }
        }

        private void Use(IdentifierExpression id)
        {
            if (_state == null || !_reporting ||
                !_state.Tokens.TryGetValue(key: id.Name, value: out TokenState? t) || t.EndedBy == null)
            {
                return;
            }

            string key = $"{id.Name}@{id.Location.Line}:{id.Location.Column}";
            if (!_reported.Add(item: key))
            {
                return;
            }

            owner.Report(code: SemanticDiagnosticCode.TokenUsedAfterSourceChanged,
                message:
                $"You are using the token '{id.Name}', but {t.EndedBy} (line {t.EndedAt?.Line}). '{id.Name}' no " +
                "longer points at a live object. Take the token again after that line, or use it before.",
                location: id.Location);
        }

        // -- statements --

        private void Statement(Statement stmt)
        {
            if (_state == null)
            {
                return;
            }

            int index = ++_statementIndex;
            switch (stmt)
            {
                case BlockStatement block:
                    _blockLocals.Push(item: []);
                    foreach (Statement s in block.Statements)
                    {
                        Statement(stmt: s);
                    }

                    CloseBlock(at: block.Location);
                    return;
                case DangerStatement danger:
                    Statement(stmt: danger.Body);
                    return;
                case DeclarationStatement { Declaration: VariableDeclaration v }:
                    if (v.Initializer != null)
                    {
                        Expr(expr: v.Initializer);
                    }

                    if (_reporting && IsSourceIterable(type: v.Initializer?.ResolvedType) &&
                        !v.Name.StartsWith(value: "_lf_", comparisonType: StringComparison.Ordinal))
                    {
                        owner.ReportSourceIterableKept(type: v.Initializer!.ResolvedType!,
                            where: $"in '{v.Name}'",
                            location: v.Location);
                    }

                    // `each item in v` over a read token starts with `var _lf_iter_N = v.iter()`.
                    if (v.Initializer is CallExpression
                        {
                            Callee: MemberExpression { Object: var iterated, MemberName: "iter" }
                        } && (IsReadToken(type: iterated.ResolvedType) ||
                              iterated is IdentifierExpression { Name: var readName } &&
                              _readElements.Contains(item: readName)))
                    {
                        _readIterators.Add(item: v.Name);
                    }

                    if (IsTrackedToken(type: v.Initializer?.ResolvedType))
                    {
                        Bind(name: v.Name, value: v.Initializer!, index: index);
                    }

                    _blockLocals.TryPeek(result: out List<string>? locals);
                    locals?.Add(item: v.Name);
                    EndTemporaries(index: index, at: stmt.Location);
                    return;
                case AssignmentStatement assign:
                    Expr(expr: assign.Value);
                    Assign(target: assign.Target, value: assign.Value, index: index, at: assign.Location);
                    EndTemporaries(index: index, at: stmt.Location);
                    return;
                case ReturnStatement ret:
                    if (ret.Value != null)
                    {
                        Expr(expr: ret.Value);
                        if (_reporting && IsSourceIterable(type: ret.Value.ResolvedType))
                        {
                            owner.ReportSourceIterableKept(type: ret.Value.ResolvedType!,
                                where: "by returning it",
                                location: ret.Location);
                        }
                    }

                    _state = null;
                    return;
                case ThrowStatement or AbsentStatement:
                    if (stmt is ThrowStatement t)
                    {
                        Expr(expr: t.Error);
                    }

                    _state = null;
                    return;
                case BreakStatement:
                    if (_loops.TryPeek(result: out (List<State?> Breaks, List<State?> Continues) loopB))
                    {
                        loopB.Breaks.Add(item: _state.Clone());
                    }

                    _state = null;
                    return;
                case ContinueStatement:
                    if (_loops.TryPeek(result: out (List<State?> Breaks, List<State?> Continues) loopC))
                    {
                        loopC.Continues.Add(item: _state.Clone());
                    }

                    _state = null;
                    return;
                case IfStatement ifs:
                {
                    Expr(expr: ifs.Condition);
                    State entry = _state.Clone();
                    Statement(stmt: ifs.ThenStatement);
                    State? afterThen = _state;
                    _state = entry;
                    if (ifs.ElseStatement is { } alt)
                    {
                        Statement(stmt: alt);
                    }

                    _state = State.Merge(a: afterThen, b: _state);
                    return;
                }
                case WhenStatement whenStmt:
                {
                    Expr(expr: whenStmt.Expression);
                    State entry = _state.Clone();
                    State? merged = entry.Clone();
                    // The step of an `each` loop over a read token (`try _lf_iter_N.emit()`) binds a read-only element.
                    Expression step = whenStmt.Expression is RecoveryExpression recovery
                        ? recovery.LoweredCall ?? recovery.Inner
                        : whenStmt.Expression;
                    bool readStep = step is CallExpression
                    {
                        Callee: MemberExpression { Object: IdentifierExpression { Name: var stepIter } }
                    } && _readIterators.Contains(item: stepIter);
                    foreach (WhenClause clause in whenStmt.Clauses)
                    {
                        _state = entry.Clone();
                        string? element = readStep && clause.Pattern is ElsePattern { VariableName: { } bound }
                            ? bound
                            : null;
                        bool added = element != null && _readElements.Add(item: element);
                        Statement(stmt: clause.Body);
                        if (added)
                        {
                            _readElements.Remove(item: element!);
                        }

                        merged = State.Merge(a: merged, b: _state);
                    }

                    _state = merged;
                    return;
                }
                case WhileStatement loop:
                    Loop(condition: loop.Condition, body: loop.Body);
                    if (loop.ElseBranch is { } elseBranch)
                    {
                        Statement(stmt: elseBranch);
                    }

                    return;
                case LoopStatement loop:
                    Loop(condition: null, body: loop.Body);
                    return;
                case UsingStatement usingStmt:
                {
                    Expr(expr: usingStmt.Resource);
                    State beforeBinding = _state.Clone();
                    _blockLocals.Push(item: []);
                    if (IsTrackedToken(type: usingStmt.Resource.ResolvedType))
                    {
                        Bind(name: usingStmt.Name, value: usingStmt.Resource, index: index);
                    }

                    Statement(stmt: usingStmt.Body);
                    CloseBlock(at: usingStmt.Location);
                    _state?.Tokens.Remove(key: usingStmt.Name);
                    if (usingStmt.FallbackBody is { } fallback)
                    {
                        State? afterBody = _state;
                        _state = beforeBinding;
                        Statement(stmt: fallback);
                        _state = State.Merge(a: afterBody, b: _state);
                    }

                    return;
                }
                default:
                    foreach (Expression e in OwnExpressions(stmt: stmt))
                    {
                        Expr(expr: e);
                    }

                    EndTemporaries(index: index, at: stmt.Location);
                    return;
            }
        }

        private static List<Expression> OwnExpressions(Statement stmt)
        {
            return stmt switch
            {
                ExpressionStatement s => [s.Expression],
                DiscardStatement s => [s.Expression],
                DestructuringStatement s => [s.Initializer],
                _ => []
            };
        }

        private void Loop(Expression? condition, Statement body)
        {
            State entry = _state!.Clone();
            State head = entry;
            bool reporting = _reporting;
            (List<State?> Breaks, List<State?> Continues) exits = ([], []);

            // Walk the loop until the token states at its head stop changing, then once more to report.
            for (int round = 0; round < 20; round++)
            {
                _reporting = false;
                exits = ([], []);
                _loops.Push(item: exits);
                _state = head.Clone();
                if (condition != null)
                {
                    Expr(expr: condition);
                }

                Statement(stmt: body);
                _loops.Pop();
                State? next = State.Merge(a: entry, b: _state);
                foreach (State? c in exits.Continues)
                {
                    next = State.Merge(a: next, b: c);
                }

                if (next == null || next.SameAs(other: head))
                {
                    break;
                }

                head = next;
            }

            _reporting = reporting;
            exits = ([], []);
            _loops.Push(item: exits);
            _state = head.Clone();
            if (condition != null)
            {
                Expr(expr: condition);
            }

            State? afterCondition = condition != null
                ? _state?.Clone()
                : null;
            Statement(stmt: body);
            _loops.Pop();

            State? after = afterCondition;
            foreach (State? b in exits.Breaks)
            {
                after = State.Merge(a: after, b: b);
            }

            _state = after;
        }

        private void CloseBlock(SourceLocation at)
        {
            List<string> locals = _blockLocals.Pop();
            foreach (string name in locals)
            {
                End(written: name, why: "went out of scope when its block ended", at: at);
            }

            if (_state != null)
            {
                foreach (string name in locals)
                {
                    _state.Tokens.Remove(key: name);
                }
            }
        }

        private void Bind(string name, Expression value, int index)
        {
            if (_state == null)
            {
                return;
            }

            HashSet<string> origins = owner.OriginsOf(expr: value, tokenOrigins: Held) ?? [$"~{index}"];
            _state.Tokens[key: name] = new TokenState(Origins: origins, EndedBy: null, EndedAt: null);
        }

        private void EndTemporaries(int index, SourceLocation at)
        {
            End(written: $"~{index}", why: "was a temporary, gone at the end of its statement", at: at);
        }

        private void Assign(Expression target, Expression value, int index, SourceLocation at)
        {
            if (_reporting && IsSourceIterable(type: value.ResolvedType))
            {
                owner.ReportSourceIterableKept(type: value.ResolvedType!,
                    where: target switch
                    {
                        IdentifierExpression kept => $"in '{kept.Name}'",
                        MemberExpression m => $"in the field '{m.MemberName}'",
                        _ => "in a slot"
                    },
                    location: at);
            }

            if (_reporting && target is MemberExpression && RootIdentifier(expr: target) is { } root &&
                _readElements.Contains(item: root))
            {
                owner.Report(code: SemanticDiagnosticCode.WriteThroughReadOnlyWrapper,
                    message:
                    $"You are writing to '{root}', an element of a loop over a read token. The loop only lets you " +
                    "read its elements. Loop over a write token instead ('.modify()', or a 'Modifying[T]' parameter).",
                    location: at);
            }

            if (target is IdentifierExpression id && _state?.Tokens.ContainsKey(key: id.Name) == true)
            {
                Bind(name: id.Name, value: value, index: index);
                return;
            }

            // The parts of the target that are read (an index, a receiver) are uses.
            if (target is not IdentifierExpression)
            {
                foreach (object child in AstWalker.EnumerateChildren(node: target))
                {
                    if (child is Expression e)
                    {
                        Expr(expr: e);
                    }
                }
            }

            if (owner.PathsOf(expr: target, tokenOrigins: Held) is { } written)
            {
                foreach (string w in written)
                {
                    End(written: w, why: $"was replaced by the assignment to '{Shown(path: w)}'", at: at);
                }
            }
        }

        private static string? RootIdentifier(Expression expr)
        {
            return expr switch
            {
                IdentifierExpression id => id.Name,
                MemberExpression m => RootIdentifier(expr: m.Object),
                IndexExpression ix => RootIdentifier(expr: ix.Object),
                _ => null
            };
        }

        // -- expressions --

        private void Expr(Expression expr)
        {
            switch (expr)
            {
                case IdentifierExpression id:
                    Use(id: id);
                    return;
                case LambdaExpression:
                    return;
                case NamedArgumentExpression named:
                    Expr(expr: named.Value);
                    return;
                case StealExpression steal:
                    Expr(expr: steal.Operand);
                    if (owner.PathsOf(expr: steal.Operand, tokenOrigins: Held) is { } stolen)
                    {
                        foreach (string s in stolen)
                        {
                            End(written: s, why: $"was moved out by 'steal {Shown(path: s)}'", at: steal.Location);
                        }
                    }

                    return;
                case BinaryExpression { Operator: BinaryOperator.Assign, Left: var left, Right: var right }:
                    Expr(expr: right);
                    Assign(target: left, value: right, index: _statementIndex, at: expr.Location);
                    return;
                case CallExpression call:
                    Call(call: call);
                    return;
                default:
                    Children(node: expr);
                    return;
            }
        }

        /// <summary>Walks the expressions under <paramref name="node"/> in order, through parts that are not
        /// expressions themselves (an f-text's pieces, a pattern).</summary>
        private void Children(object node)
        {
            foreach (object child in AstWalker.EnumerateChildren(node: node))
            {
                if (child is Expression e)
                {
                    Expr(expr: e);
                }
                else if (child is not SyntaxTree.Statement)
                {
                    Children(node: child);
                }
            }
        }

        private void Call(CallExpression call)
        {
            List<string>? outer = _callEnds;
            var ends = new List<string>();
            _callEnds = ends;

            if (call.Callee is MemberExpression { Object: var receiver })
            {
                Expr(expr: receiver);
            }
            else
            {
                Expr(expr: call.Callee);
            }

            foreach (Expression arg in call.Arguments)
            {
                Expr(expr: arg);
            }

            // The tokens this call is handed (its receiver and arguments) are in use while it runs.
            var inUse = new List<(string Origin, Expression Operand)>();
            void Collect(Expression operand)
            {
                Expression value = Unwrap(expr: operand);
                if (!IsTrackedToken(type: value.ResolvedType))
                {
                    // A mint whose receiver was moved out in this same call has no type left, but it is still
                    // the token handed to the call.
                    if (value is CallExpression
                        {
                            Callee: MemberExpression { Object: var minted, MemberName: "view" or "modify" }
                        } && owner.PathsOf(expr: minted, tokenOrigins: Held) is { } mintedFrom)
                    {
                        inUse.AddRange(collection: mintedFrom.Select(selector: o => (o, value)));
                    }

                    return;
                }

                foreach (string o in owner.OriginsOf(expr: value, tokenOrigins: Held) ?? [])
                {
                    inUse.Add(item: (o, value));
                }
            }

            if (call.Callee is MemberExpression { Object: var r })
            {
                Collect(operand: r);
            }

            foreach (Expression arg in call.Arguments)
            {
                Collect(operand: arg);
            }

            if (_reporting)
            {
                owner.CheckReadTokenWrites(call: call,
                    isReadElement: e => e is IdentifierExpression { Name: var n } && _readElements.Contains(item: n));
            }

            List<(string Path, string Callee)> writes = owner.CallWrites(call: call, tokenOrigins: Held);
            if (_reporting && inUse.Count > 0)
            {
                // A `steal` among the arguments is RF-S615 at the stolen variable's other use in the call (the
                // builder's own use-after-steal check says it moves out in the same call), so only the call's own
                // effects are checked here.
                foreach ((string path, string callee) in writes)
                {
                    if (inUse.FirstOrDefault(predicate: u => u.Origin != UnknownOrigin && Ends(written: path, origin: u.Origin)) is
                        { Operand: not null } hit)
                    {
                        string by = callee.Length > 0
                            ? $"'{callee}()' can replace '{Shown(path: path)}'"
                            : $"a routine value it is handed may replace '{Shown(path: path)}'";
                        owner.Report(code: SemanticDiagnosticCode.TokenSourceReplaced,
                            message:
                            $"This call is handed a token taken from '{Shown(path: hit.Origin)}', but {by} while the " +
                            "call is still using that token, which would leave it pointing at freed memory. Change it " +
                            "in a separate statement, before or after the call.",
                            location: call.Location);
                        break;
                    }
                }
            }

            _callEnds = outer;
            foreach ((string path, string callee) in writes)
            {
                string why = callee.Length == 0
                    ? $"may have been replaced by a call through a routine value that is handed '{Shown(path: path)}'"
                    : IsDeep(path: path)
                        ? $"may have changed: '{callee}()' can change what is inside '{Shown(path: path)}'"
                        : $"may have been replaced by '{callee}()', which replaces '{Shown(path: path)}'";
                End(written: path, why: why, at: call.Location);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a lazy iterable that points at the collection it was made from: an adapter
    /// such as <c>WhereIterable[T, S]</c>, which keeps its source as a <c>Hijacked[S]</c> field, <c>S</c> being one of
    /// its own type parameters with <c>needs S obeys Iterable[…]</c>. A range or a generator keeps its own state and
    /// points at nothing, so it is not one.
    /// </summary>
    internal static bool IsSourceIterable(TypeSymbol? type)
    {
        TypeSymbol? def = type switch
        {
            EntityTypeSymbol { GenericDefinition: { } entityDef } => entityDef,
            RecordTypeSymbol { GenericDefinition: { } recordDef } => recordDef,
            _ => null
        };
        List<MemberVariableInfo>? fields = def switch
        {
            EntityTypeSymbol e => e.MemberVariables,
            RecordTypeSymbol r => r.MemberVariables,
            _ => null
        };
        if (def?.GenericConstraints is not { } constraints || fields == null)
        {
            return false;
        }

        HashSet<string> iterableParams = constraints
                                         .Where(predicate: c => c.ConstraintType == ConstraintKind.Obeys &&
                                                                (c.ConstraintTypes ?? []).Any(predicate: t =>
                                                                    t.Name == "Iterable"))
                                         .Select(selector: c => c.ParameterName)
                                         .ToHashSet(comparer: StringComparer.Ordinal);
        return fields.Any(predicate: f => f.Type.BareName == RuntimeContract.Hijacked &&
                                          f.Type.TypeArguments is [GenericParameterTypeSymbol held] &&
                                          iterableParams.Contains(item: held.Name));
    }

    private void ReportSourceIterableKept(TypeSymbol type, string where, SourceLocation location)
    {
        report(code: SemanticDiagnosticCode.SourceIterableKept,
            message:
            $"You are keeping a lazy '{type.BareName}' {where}. It points at the collection it was made from, so it is " +
            "used in the statement that makes it: loop over it with 'each', finish it with a terminal call " +
            "('.List()', '.first()', '.get_count()', …), or pass it straight to a routine. To use the result more than " +
            "once, keep '.List()' of it.",
            location: location);
    }

    /// <summary>Whether a token only lets its holder read: <c>Viewing[T]</c>, or the lock-held <c>Consulting[T]</c>.</summary>
    private static bool IsReadToken(TypeSymbol? type)
    {
        return type?.BareName is RuntimeContract.Viewing or RuntimeContract.Consulting;
    }

    /// <summary>
    /// RF-S456: a call that can change what a READ token points at, through that token: the receiver
    /// (`v.set_to(n: 2)`, `xs.view().add_last(...)`) or an argument handed to a parameter the callee changes. A
    /// routine changes a slot when its body writes into it or hands it on to something that does (the same
    /// summary that tells which parts a routine may replace), and a stdlib routine when it is not `@readonly`.
    /// Taking a write token through a read one (`v.modify_at(...)`) is rejected the same way. Reads, and routines
    /// that only read, go through.
    /// </summary>
    private void CheckReadTokenWrites(CallExpression call, Func<Expression, bool> isReadElement)
    {
        if (IsRoutineValueCall(call: call) || call.ResolvedRoutine is not { } callee)
        {
            return;
        }

        Dictionary<string, HashSet<string>> effects = CalleeEffects(callee: callee);
        bool mintsWriteToken = callee.ReturnType?.BareName is RuntimeContract.Modifying or RuntimeContract.Amending;
        if (TakesReceiver(callee: callee) && call.Callee is MemberExpression { Object: var receiver } &&
            (IsReadToken(type: receiver.ResolvedType) || isReadElement(arg: receiver)) &&
            (effects.ContainsKey(key: MeSlot) || mintsWriteToken))
        {
            string through = IsReadToken(type: receiver.ResolvedType)
                ? $"a read token ('{receiver.ResolvedType!.Name}')"
                : $"'{(receiver as IdentifierExpression)?.Name}', an element of a loop over a read token";
            report(code: SemanticDiagnosticCode.WritableMemberRoutineThroughReadOnlyWrapper,
                message:
                $"You are calling '{callee.Name}()' through {through}, but " +
                (mintsWriteToken
                    ? $"'{callee.Name}()' hands out a write token on it."
                    : IsReadToken(type: receiver.ResolvedType)
                        ? $"'{callee.Name}()' can change what the token points at."
                        : $"'{callee.Name}()' can change it.") +
                " A read token only lets you read. Take a write token instead ('.modify()', or a 'Modifying[T]' " +
                "parameter), or mark the routine @readonly if it does not change anything.",
                location: call.Location);
            return;
        }

        foreach ((string param, Expression arg) in BoundArguments(call: call, callee: callee))
        {
            // A read-token parameter is checked inside the callee: a write through it is reported there.
            if (callee.Parameters.FirstOrDefault(predicate: p => p.Name == param) is { } declared &&
                IsReadToken(type: declared.Type))
            {
                continue;
            }

            if (IsReadToken(type: arg.ResolvedType) && effects.ContainsKey(key: param))
            {
                report(code: SemanticDiagnosticCode.WritableMemberRoutineThroughReadOnlyWrapper,
                    message:
                    $"You are handing a read token ('{arg.ResolvedType!.Name}') to '{param}' of '{callee.Name}', " +
                    $"which can change it. A read token only lets you read: pass a write token instead.",
                    location: arg.Location);
                return;
            }
        }
    }

    private void Report(SemanticDiagnosticCode code, string message, SourceLocation location)
    {
        report(code: code, message: message, location: location);
    }
}
