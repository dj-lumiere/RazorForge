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
/// routine that returns a token gets a RETURN ORIGIN: the parameters (and <c>me</c>) it may take the token from, each
/// with the part of it. Different returns may use different parameters. A caller's token has every argument handed to
/// one of them as a source, so a change that ends any of them ends the token. A return from a local, a temporary or
/// an entity parameter the routine owns is RF-S600. Both summaries are solved as a fixpoint over all user
/// routines.</para>
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

    /// <summary>Whether a block's target is a value made in its head (a call, not a name or a member path): the block
    /// holds it alone, so its token comes from nothing outside the block.</summary>
    private static bool MadeInHead(AccessTarget target)
    {
        Expression e = target.Target;
        while (e is MemberExpression member)
        {
            e = member.Object;
        }

        return e is not IdentifierExpression;
    }

    /// <summary>The suffix for "somewhere inside": <c>h.*</c> is any part of <c>h</c> below it.</summary>
    private const string Inside = ".*";

    private readonly Dictionary<string, RoutineDeclaration> _userRoutineDeclarations =
        new(comparer: StringComparer.Ordinal);

    private readonly Dictionary<RoutineDeclaration, Dictionary<string, HashSet<string>>> _replaceEffects =
        new(comparer: ReferenceEqualityComparer.Instance);

    /// <summary>The return origin of each token-returning user routine: the slots it may take the token from, each
    /// with the part of it. Empty while the fixpoint has found none yet (a routine that only returns what a call to
    /// itself returns), null when a return takes it from something that is not a slot.</summary>
    private readonly Dictionary<RoutineDeclaration, HashSet<(string Slot, string Suffix)>?> _returnOrigins =
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

    /// <summary>The slots a call to <paramref name="callee"/> may take its returned token from, each with the part
    /// of it. Null when the callee returns no token.</summary>
    private HashSet<(string Slot, string Suffix)>? CalleeReturnOrigin(RoutineInfo callee)
    {
        if (!IsTrackedToken(type: callee.ReturnType))
        {
            return null;
        }

        if (FindUserDeclaration(routine: callee) is { } decl &&
            _returnOrigins.TryGetValue(key: decl, value: out HashSet<(string, string)>? known))
        {
            return known ?? [(MeSlot, Inside)];
        }

        return callee.Name switch
        {
            "view" or "modify" => [(MeSlot, "")],
            "view_at" or "modify_at" => [(MeSlot, "[]")],
            _ when callee.OwnerType != null => [(MeSlot, Inside)],
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
            // The token comes from every argument handed to a slot the callee may take it from: a change that ends
            // any one of them ends the token.
            case CallExpression { ResolvedRoutine: { } callee } call when !IsRoutineValueCall(call: call) &&
                                                                         CalleeReturnOrigin(callee: callee) is
                                                                             { } froms:
            {
                var origins = new HashSet<string>(comparer: StringComparer.Ordinal);
                foreach ((string slot, string suffix) in froms)
                {
                    Expression? source = slot == MeSlot
                        ? (call.Callee as MemberExpression)?.Object
                        : BoundArguments(call: call, callee: callee)
                         .Where(predicate: b => b.Param == slot)
                         .Select(selector: b => b.Arg)
                         .FirstOrDefault();
                    if (source == null)
                    {
                        origins.Add(item: UnknownOrigin);
                        continue;
                    }

                    // A temporary among them makes the token a temporary: it ends with the statement.
                    if (PathsOf(expr: source, tokenOrigins: tokenOrigins) is not { } bases)
                    {
                        return null;
                    }

                    origins.UnionWith(other: bases.Select(selector: b => Join(path: b, suffix: suffix)));
                }

                return origins;
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
            _returnOrigins.TryAdd(key: decl, value: []);
        }

        bool changed = true;
        int rounds = 0;
        while (changed && rounds++ < 50)
        {
            changed = false;
            foreach (RoutineDeclaration decl in _userRoutineDeclarations.Values)
            {
                Dictionary<string, HashSet<string>> effects = _replaceEffects[key: decl];
                (Dictionary<string, HashSet<string>> found, HashSet<(string, string)>? ret) =
                    DirectSummary(routine: decl);
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

                HashSet<(string, string)>? was = _returnOrigins[key: decl];
                if (was == null != (ret == null) || was != null && !was.SetEquals(other: ret!))
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
                        // A `viewing` / `modifying` block's token comes from its target: a Guarded, a token, an entity.
                        case AccessStatement access:
                            foreach (AccessTarget target in access.Targets)
                            {
                                // A target made in the head (a call) is held by the block alone.
                                Add(name: target.Name, value: MadeInHead(target: target) ? null : target.Target);
                            }

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
    private (Dictionary<string, HashSet<string>> Effects, HashSet<(string, string)>? Return) DirectSummary(
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
                    case DropStatement { EndsBinding: true } ended:
                        RecordWrite(target: ended.Expression);
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

        HashSet<(string, string)>? ret = [];
        if (IsTrackedToken(type: routine.ResolvedInfo?.ReturnType))
        {
            ret = ReturnSummary(routine: routine, slots: ReturnSlots(routine: routine), lookup: Lookup).Origin;
        }

        return (effects, ret);
    }

    /// <summary>The parts a call may replace, as absolute paths, each with the name of the routine doing it.</summary>
    /// <summary>The paths a call writes through its own receiver (the parts of <c>me</c> its callee replaces).</summary>
    private HashSet<string> ReceiverWrites(CallExpression call, RoutineInfo callee,
        Dictionary<string, HashSet<string>> effects, Func<string, HashSet<string>?> tokenOrigins)
    {
        var writes = new HashSet<string>(comparer: StringComparer.Ordinal);
        if (effects.TryGetValue(key: MeSlot, value: out HashSet<string>? meParts) && TakesReceiver(callee: callee) &&
            call.Callee is MemberExpression { Object: var receiver } &&
            PathsOf(expr: receiver, tokenOrigins: tokenOrigins) is { } receivers)
        {
            foreach (string r in receivers)
            {
                foreach (string part in meParts)
                {
                    writes.Add(item: Join(path: r, suffix: part));
                }
            }
        }

        return writes;
    }

    /// <summary>The paths <paramref name="call"/> writes through its own receiver, for a call that is not made
    /// through a routine value.</summary>
    private HashSet<string> ReceiverWrites(CallExpression call, Func<string, HashSet<string>?> tokenOrigins)
    {
        return IsRoutineValueCall(call: call) || call.ResolvedRoutine is not { } callee
            ? []
            : ReceiverWrites(call: call, callee: callee, effects: CalleeEffects(callee: callee),
                tokenOrigins: tokenOrigins);
    }

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

        foreach (string path in ReceiverWrites(call: call, callee: callee, effects: effects, tokenOrigins: tokenOrigins))
        {
            writes.Add(item: (path, callee.Name));
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

    /// <summary>The return origin of a token-returning routine (null when a return takes the token from something
    /// that is not one of <paramref name="slots"/>), and every return with each source it may take the token from.</summary>
    private (HashSet<(string, string)>? Origin, List<(ReturnStatement Ret, string Source)> Returns) ReturnSummary(
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

        if (returns.Any(predicate: r => !slots.Contains(item: RootName(path: r.Item2))))
        {
            return (null, returns);
        }

        return (returns.Select(selector: r => (RootName(path: r.Item2), Suffix(path: r.Item2)))
                       .ToHashSet(), returns);
    }

    /// <summary>RF-S600: every return of a token-returning routine takes the token from <c>me</c> or from a token
    /// parameter (different returns may use different ones), never from a local, a temporary, or an entity parameter
    /// the routine owns.</summary>
    private void CheckReturns(RoutineDeclaration routine)
    {
        if (!IsTrackedToken(type: routine.ResolvedInfo?.ReturnType))
        {
            return;
        }

        HashSet<string> slots = ReturnSlots(routine: routine);
        HashSet<string> owned = Slots(routine: routine);
        Dictionary<string, HashSet<string>> tokenVars = FlowInsensitiveTokenOrigins(routine: routine);
        (HashSet<(string, string)>? origin, List<(ReturnStatement Ret, string Source)> returns) =
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
                    ? $"'{Shown(path: local.Source)}', an entity parameter handed over to this routine"
                    : $"'{Shown(path: local.Source)}', which belongs to this routine";
            report(code: SemanticDiagnosticCode.TokenReturnNotAllowed,
                message:
                $"You are returning a token taken from {what}. It is gone when the routine returns, so the token " +
                "would point at freed memory. Return a token taken from 'me' or from a token parameter " +
                "(declare it Viewing[T] or Modifying[T], callers can still pass the entity itself).",
                location: local.Ret.Location);
        }
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

        /// <summary>The iterators of `for` loops over a read token (`for item in v` with `v: Viewing[List[T]]`).</summary>
        private readonly HashSet<string> _readIterators = new(comparer: StringComparer.Ordinal);

        /// <summary>The loop variables those iterators hand out, while their loop body runs: an element seen
        /// through a read token is read-only too.</summary>
        private readonly HashSet<string> _readElements = new(comparer: StringComparer.Ordinal);

        /// <summary>The containers each `for` loop's iterator (`_lf_iter_N`) walks, as source paths.</summary>
        private readonly Dictionary<string, HashSet<string>> _iteratorSources = new(comparer: StringComparer.Ordinal);

        /// <summary>The step value of a `for` loop (`_lf_elem_M` of `for (i, d) in xs.enumerate()`) with the
        /// containers its tokens come from: `d`, read as `_lf_elem_M.item1`, is an element of `xs`.</summary>
        private readonly Dictionary<string, HashSet<string>> _stepElements = new(comparer: StringComparer.Ordinal);
        private readonly Stack<(List<State?> Breaks, List<State?> Continues)> _loops = new();
        private readonly Stack<List<string>> _blockLocals = new();
        private State? _state;
        private bool _reporting = true;
        private int _statementIndex;
        private List<string>? _callEnds;

        /// <summary>The names that may reach the same entity as another name of the same entity type without the
        /// builder knowing it: token parameters, <c>me</c>, and <c>Retained</c> handles (a copy of one is the same
        /// entity). Each maps to the entity type it reaches, and a change through one of them ends the tokens taken
        /// from the same part of every other.</summary>
        private readonly Dictionary<string, string> _aliasRoots = new(comparer: StringComparer.Ordinal);

        /// <summary>The names a pattern bound to what an identifier being matched holds, with that identifier: giving
        /// the identifier a new value there is RF-S264 already, so it does not end them a second time.</summary>
        private readonly Dictionary<string, string> _pinnedSubjects = new(comparer: StringComparer.Ordinal);

        /// <summary>The pairs of names a <c>viewing</c> / <c>modifying</c> block with several targets is running over:
        /// the block checks they are different entities, so a change through one does not reach the other.</summary>
        private readonly HashSet<(string, string)> _distinct = [];

        /// <summary>The names the routine's body uses as a whole value somewhere (handed on, copied, stolen), not
        /// only to reach into (<c>r.inner</c>).</summary>
        private HashSet<string> BareNames => _bareNames ??= CollectBareNames();

        private HashSet<string>? _bareNames;

        private HashSet<string> CollectBareNames()
        {
            var reachedThrough = new HashSet<IdentifierExpression>(comparer: ReferenceEqualityComparer.Instance);
            AstWalker.WalkExpressions(root: routine.Body, visit: e =>
            {
                if (e is MemberExpression { Object: IdentifierExpression through })
                {
                    reachedThrough.Add(item: through);
                }
                else if (e is CallExpression { Callee: MemberExpression { Object: IdentifierExpression receiver } })
                {
                    reachedThrough.Add(item: receiver);
                }
            });
            var bare = new HashSet<string>(comparer: StringComparer.Ordinal);
            AstWalker.WalkExpressions(root: routine.Body, visit: e =>
            {
                if (e is IdentifierExpression id && !reachedThrough.Contains(item: id))
                {
                    bare.Add(item: id.Name);
                }
            });
            return bare;
        }

        public void Run()
        {
            var start = new Dictionary<string, TokenState>(comparer: StringComparer.Ordinal);
            if (routine.ResolvedInfo is { } info)
            {
                foreach (ParamInfo p in info.Parameters.Where(predicate: p => IsTrackedToken(type: p.Type)))
                {
                    start[key: p.Name] = new TokenState(Origins: [p.Name], EndedBy: null, EndedAt: null);
                }

                foreach (ParamInfo p in info.Parameters)
                {
                    if (AliasKey(type: p.Type) is { } key)
                    {
                        _aliasRoots[key: p.Name] = key;
                    }
                }

                if (info.OwnerType is EntityTypeSymbol owner && info.Kind is not (RoutineKind.CommonRoutine or RoutineKind.Creator))
                {
                    _aliasRoots[key: MeSlot] = owner.Name;
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

            // A change through a name that may be the same entity as another one (two token parameters, two copies of
            // a Retained handle) changes that part of the other as well.
            var writes = new List<(string Path, string Why)> { (written, why) };
            string root = RootName(path: written);
            string suffix = Suffix(path: written);
            if (suffix.Length > 0 && _aliasRoots.TryGetValue(key: root, value: out string? key))
            {
                foreach ((string other, string otherKey) in _aliasRoots)
                {
                    if (other != root && otherKey == key && !_distinct.Contains(item: (root, other)))
                    {
                        writes.Add(item: (Join(path: other, suffix: suffix),
                            $"may have been changed through '{root}', which may be the same {key} as '{other}' " +
                            $"({why.Replace(oldValue: "was ", newValue: "", comparisonType: StringComparison.Ordinal)})"));
                    }
                }
            }

            foreach ((string path, string reason) in writes)
            {
                foreach ((string name, TokenState t) in _state.Tokens.ToList())
                {
                    if (t.EndedBy == null &&
                        !(_pinnedSubjects.TryGetValue(key: name, value: out string? subject) && subject == path) &&
                        t.Origins.FirstOrDefault(predicate: o => Ends(written: path, origin: o)) is { } ended)
                    {
                        string from = ended == UnknownOrigin || ended.StartsWith(value: "~", comparisonType: StringComparison.Ordinal)
                            ? "the object it was taken from"
                            : $"'{Shown(path: ended)}', where it was taken from,";
                        _state.Tokens[key: name] = t with { EndedBy = $"{from} {reason}", EndedAt = at };
                    }
                }
            }
        }

        /// <summary>The entity type a value of <paramref name="type"/> may share with another name: the inner type of a
        /// token or a <c>Retained</c> handle. Null for anything else.</summary>
        private static string? AliasKey(TypeSymbol? type)
        {
            return type?.BareName is RuntimeContract.Viewing or RuntimeContract.Modifying or RuntimeContract.Retained &&
                   type.TypeArguments is [EntityTypeSymbol inner]
                ? inner.Name
                : null;
        }

        /// <summary>The names a pattern binds, each with the part of the matched value it names (empty for the whole
        /// value, <c>.item</c> for <c>Chest(item: as c)</c>).</summary>
        private static IEnumerable<(string Name, string Suffix)> PatternBindings(Pattern? pattern, string suffix)
        {
            switch (pattern)
            {
                case ElsePattern { VariableName: { } rest }:
                    yield return (rest, suffix);
                    break;
                case TypePattern typed:
                    if (typed.VariableName is { } whole)
                    {
                        yield return (whole, suffix);
                    }

                    foreach ((string, string) b in MemberBindings(bindings: typed.Bindings, suffix: suffix))
                    {
                        yield return b;
                    }

                    break;
                case TypeDestructuringPattern destructured:
                    foreach ((string, string) b in MemberBindings(bindings: destructured.Bindings, suffix: suffix))
                    {
                        yield return b;
                    }

                    break;
                case DestructuringPattern nested:
                    foreach ((string, string) b in MemberBindings(bindings: nested.Bindings, suffix: suffix))
                    {
                        yield return b;
                    }

                    break;
                case GuardPattern guarded:
                    foreach ((string, string) b in PatternBindings(pattern: guarded.InnerPattern, suffix: suffix))
                    {
                        yield return b;
                    }

                    break;
            }
        }

        private static IEnumerable<(string Name, string Suffix)> MemberBindings(List<DestructuringBinding>? bindings,
            string suffix)
        {
            foreach (DestructuringBinding b in bindings ?? [])
            {
                string part = b.MemberVariableName is { } member
                    ? suffix + "." + member
                    : suffix;
                if (b.NestedPattern != null)
                {
                    foreach ((string, string) inner in PatternBindings(pattern: b.NestedPattern, suffix: part))
                    {
                        yield return inner;
                    }
                }
                else if ((b.BindingName ?? b.MemberVariableName) is { } name)
                {
                    yield return (name, part);
                }
            }
        }

        /// <summary>Whether the name <paramref name="name"/> in <paramref name="body"/> reads an entity in place (an
        /// entity or a token on one): its first use there says the type the pattern gave it.</summary>
        private static bool NamesEntityInPlace(Statement body, string name)
        {
            TypeSymbol? found = null;
            AstWalker.WalkExpressions(root: body, visit: e =>
            {
                if (found == null && e is IdentifierExpression { Name: var n, ResolvedType: { } t } && n == name)
                {
                    found = t;
                }
            });
            // A token on a value element reads the slot, which a replacement fills again: only an entity there is
            // torn down by it.
            return found is EntityTypeSymbol ||
                   IsTrackedToken(type: found) && found!.TypeArguments is [EntityTypeSymbol];
        }

        /// <summary>Takes each name <paramref name="pattern"/> binds to an entity in place as a token on the part of
        /// <paramref name="matched"/> it names, for <paramref name="body"/>: replacing, stealing or dropping that part
        /// there ends it (RF-S643 at a later use). Gives the names it bound.</summary>
        private List<string> BindPattern(Pattern? pattern, HashSet<string> matched, Statement body, string? subject)
        {
            var bound = new List<string>();
            if (_state == null)
            {
                return bound;
            }

            foreach ((string name, string part) in PatternBindings(pattern: pattern, suffix: ""))
            {
                if (!NamesEntityInPlace(body: body, name: name))
                {
                    continue;
                }

                _state.Tokens[key: name] = new TokenState(
                    Origins: matched.Select(selector: m => Join(path: m, suffix: part))
                                    .ToHashSet(),
                    EndedBy: null,
                    EndedAt: null);
                if (subject != null && part.Length == 0)
                {
                    _pinnedSubjects[key: name] = subject;
                }

                bound.Add(item: name);
            }

            return bound;
        }

        private void UnbindPattern(List<string> bound, State entry)
        {
            foreach (string name in bound)
            {
                _pinnedSubjects.Remove(key: name);
                if (_state == null)
                {
                    continue;
                }

                if (entry.Tokens.TryGetValue(key: name, value: out TokenState? before))
                {
                    _state.Tokens[key: name] = before;
                }
                else
                {
                    _state.Tokens.Remove(key: name);
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

                    // An emitter kept in a local (`var it = xs.emitter()`, `var big = xs.where(...)`) that points at
                    // the container it walks is a token on that container's elements: it ends when the container may
                    // have changed shape or ends, and a later use is RF-S643.
                    bool walkToken = !v.Name.StartsWith(value: "_lf_", comparisonType: StringComparison.Ordinal) &&
                                     v.Initializer is { } walkInit && IsWalkToken(walk: walkInit);

                    // `for item in v` over a read token starts with `var _lf_iter_N = v.iter()`.
                    if (v.Initializer is CallExpression
                        {
                            Callee: MemberExpression { Object: var iterated, MemberName: "emitter" }
                        } && (IsReadToken(type: iterated.ResolvedType) ||
                              iterated is IdentifierExpression { Name: var readName } &&
                              _readElements.Contains(item: readName)))
                    {
                        _readIterators.Add(item: v.Name);
                    }

                    // A `for` loop over a chain (`xs.enumerate()`) names the chain first (`_lf_src_N`).
                    if (v.Name.StartsWith(value: "_lf_src_", comparisonType: StringComparison.Ordinal) &&
                        v.Initializer is { } chain && WalkedContainers(walked: chain) is { } chainPaths)
                    {
                        _iteratorSources[key: v.Name] = chainPaths;
                    }

                    // A `for` loop's iterator remembers what it walks, so a token in its step value comes from there.
                    if (v.Initializer is CallExpression
                        {
                            Callee: MemberExpression { Object: var walked, MemberName: "emitter" }
                        } && WalkedContainers(walked: walked) is { } walkedPaths)
                    {
                        _iteratorSources[key: v.Name] = walkedPaths;
                    }

                    // A handle made here (`Retained(from: ...)`) and only ever reached through (`r.inner`) is no other
                    // name's entity. A copy, a handle handed out or one that came from somewhere may be.
                    if (v.Initializer?.ResolvedType is { BareName: RuntimeContract.Retained } handle &&
                        AliasKey(type: handle) is { } handleKey &&
                        (v.Initializer is not CallExpression { ResolvedRoutine.Kind: RoutineKind.Creator } ||
                         BareNames.Contains(item: v.Name)))
                    {
                        _aliasRoots[key: v.Name] = handleKey;
                    }

                    if (walkToken)
                    {
                        BindWalk(name: v.Name, walk: v.Initializer!, index: index);
                    }
                    else if (IsTrackedToken(type: v.Initializer?.ResolvedType))
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
                        // An emitter made from `me` or from a token parameter goes back to the caller, whose token on
                        // what it passed there it is (as a returned token is): anything else would outlive what it walks.
                        if (_reporting && IsWalkToken(walk: ret.Value) && !WalksReturnSlots(walk: ret.Value))
                        {
                            owner.ReportSourceIterableKept(type: ret.Value.ResolvedType!,
                                where: "by returning it",
                                location: ret.Location);
                        }
                    }

                    _state = null;
                    return;
                case ThrowStatement:
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
                    // `if ch is Chest(item: as c)`: the names the pattern binds reach the matched place in the body.
                    List<string> patternBound =
                        ifs.Condition is IsPatternExpression { IsNegated: false } matchedIf &&
                        owner.PathsOf(expr: matchedIf.Expression, tokenOrigins: Held) is { } ifMatched
                            ? BindPattern(pattern: matchedIf.Pattern, matched: ifMatched, body: ifs.ThenStatement,
                                subject: matchedIf.Expression is IdentifierExpression { Name: var ifSubject }
                                    ? ifSubject
                                    : null)
                            : [];
                    Statement(stmt: ifs.ThenStatement);
                    UnbindPattern(bound: patternBound, entry: entry);
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
                    // The step of a `for` loop over a read token (`try _lf_iter_N.emit()`) binds a read-only element.
                    Expression step = whenStmt.Expression is RecoveryExpression recovery
                        ? recovery.LoweredCall ?? recovery.Inner
                        : whenStmt.Expression;
                    string? stepIter = step is CallExpression
                    {
                        Callee: MemberExpression { Object: IdentifierExpression { Name: var stepIterName } }
                    }
                        ? stepIterName
                        : null;
                    bool readStep = stepIter != null && _readIterators.Contains(item: stepIter);
                    HashSet<string>? stepSources = stepIter != null
                        ? _iteratorSources.GetValueOrDefault(key: stepIter)
                        : null;
                    // What the arms' names reach in place: an element of what a `for` loop walks, or the place
                    // being matched (`when slot.item`, `when v`).
                    HashSet<string>? matched = stepSources != null
                        ? stepSources.Select(selector: w => Join(path: w, suffix: "[]"))
                                     .ToHashSet()
                        : owner.PathsOf(expr: whenStmt.Expression, tokenOrigins: Held);
                    string? subject = whenStmt.Expression is IdentifierExpression { Name: var subjectName }
                        ? subjectName
                        : null;
                    foreach (WhenClause clause in whenStmt.Clauses)
                    {
                        _state = entry.Clone();
                        List<string> patternBound = matched != null
                            ? BindPattern(pattern: clause.Pattern, matched: matched, body: clause.Body, subject: subject)
                            : [];
                        string? bound = clause.Pattern is ElsePattern { VariableName: { } boundName }
                            ? boundName
                            : null;
                        string? element = readStep ? bound : null;
                        bool added = element != null && _readElements.Add(item: element);
                        bool stepAdded = bound != null && stepSources != null &&
                                         _stepElements.TryAdd(key: bound, value: stepSources);
                        Statement(stmt: clause.Body);
                        if (added)
                        {
                            _readElements.Remove(item: element!);
                        }

                        if (stepAdded)
                        {
                            _stepElements.Remove(key: bound!);
                        }

                        UnbindPattern(bound: patternBound, entry: entry);
                        merged = State.Merge(a: merged, b: _state);
                    }

                    _state = merged;
                    return;
                }
                case WhileStatement loop:
                    Loop(condition: loop.Condition, body: loop.Body);
                    return;
                case LoopStatement loop:
                    Loop(condition: null, body: loop.Body);
                    return;
                case UsingStatement usingStmt:
                {
                    Expr(expr: usingStmt.Resource);
                    _blockLocals.Push(item: []);
                    if (IsTrackedToken(type: usingStmt.Resource.ResolvedType))
                    {
                        Bind(name: usingStmt.Name, value: usingStmt.Resource, index: index);
                    }

                    Statement(stmt: usingStmt.Body);
                    CloseBlock(at: usingStmt.Location);
                    _state?.Tokens.Remove(key: usingStmt.Name);
                    return;
                }
                case AccessStatement access:
                {
                    foreach (AccessTarget target in access.Targets)
                    {
                        Expr(expr: target.Target);
                    }

                    _blockLocals.Push(item: []);
                    foreach (AccessTarget target in access.Targets)
                    {
                        // A target made in the head (a call) is held by the block alone: its token comes from there.
                        if (MadeInHead(target: target))
                        {
                            if (_state != null)
                            {
                                _state.Tokens[key: target.Name] = new TokenState(Origins: [$"~block{index}.{target.Name}"],
                                    EndedBy: null, EndedAt: null);
                            }

                            continue;
                        }

                        // A member path target (`modifying bank.a as x`) is the token's source: a write through the
                        // token (`x.n = 2`) changes a part of `bank.a` and ends only tokens taken from that part.
                        if (target.Target is MemberExpression && _state != null &&
                            owner.PathsOf(expr: target.Target, tokenOrigins: Held) is { Count: > 0 } targetPaths)
                        {
                            _state.Tokens[key: target.Name] = new TokenState(Origins: targetPaths, EndedBy: null,
                                EndedAt: null);
                            continue;
                        }

                        Bind(name: target.Name, value: target.Target, index: index);
                    }

                    // The block checks its targets are different entities, so in its body a change through one of
                    // them does not reach another.
                    List<string> targetRoots = access.Targets
                                                     .SelectMany(selector: t =>
                                                          owner.PathsOf(expr: t.Target, tokenOrigins: _ => null) ?? [])
                                                     .Select(selector: RootName)
                                                     .Concat(second: access.Targets.Select(selector: t => t.Name))
                                                     .Distinct()
                                                     .ToList();
                    var addedPairs = new List<(string, string)>();
                    if (access.Targets.Count > 1)
                    {
                        foreach (string ra in targetRoots)
                        {
                            foreach (string rb in targetRoots)
                            {
                                if (ra != rb && _distinct.Add(item: (ra, rb)))
                                {
                                    addedPairs.Add(item: (ra, rb));
                                }
                            }
                        }
                    }

                    Statement(stmt: access.Body);
                    foreach ((string, string) pair in addedPairs)
                    {
                        _distinct.Remove(item: pair);
                    }

                    CloseBlock(at: access.Location);
                    foreach (AccessTarget target in access.Targets)
                    {
                        _state?.Tokens.Remove(key: target.Name);
                    }

                    return;
                }
                // `steal slot.item as old [inject fresh]` moves the value out of the place: a token on it ends.
                case StealStatement stealStmt:
                    if (stealStmt.Fresh != null)
                    {
                        Expr(expr: stealStmt.Fresh);
                    }

                    if (owner.PathsOf(expr: stealStmt.Place, tokenOrigins: Held) is { } stolenPlace)
                    {
                        foreach (string sp in stolenPlace)
                        {
                            End(written: sp, why: $"was moved out by 'steal {Shown(path: sp)}'", at: stealStmt.Location);
                        }
                    }

                    EndTemporaries(index: index, at: stmt.Location);
                    return;
                // `drop x` ends `x` as `steal x` does: a token taken from it ends here.
                case DropStatement { EndsBinding: true } ended:
                    Expr(expr: ended.Expression);
                    if (owner.PathsOf(expr: ended.Expression, tokenOrigins: Held) is { } dropped)
                    {
                        foreach (string d in dropped)
                        {
                            End(written: d, why: $"was ended by 'drop {Shown(path: d)}'", at: ended.Location);
                        }
                    }

                    EndTemporaries(index: index, at: stmt.Location);
                    return;
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
                DropStatement s => [s.Expression],
                DestructuringStatement s => [s.Initializer],
                _ => []
            };
        }

        private void Loop(Expression? condition, Statement body)
        {
            State entry = _state!.Clone();
            State head = entry;
            bool reporting = _reporting;
            (List<State?> Breaks, List<State?> Continues) exits;

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

            // A token in a `for` step value (`var d = _lf_elem_M.item1`) is an element of what the loop walks.
            HashSet<string> origins = Unwrap(expr: value) is MemberExpression
            {
                Object: IdentifierExpression { Name: var stepName }
            } && _stepElements.TryGetValue(key: stepName, value: out HashSet<string>? walkedPaths)
                ? walkedPaths.Select(selector: w => Join(path: w, suffix: "[]"))
                             .ToHashSet()
                : owner.OriginsOf(expr: value, tokenOrigins: Held) ?? [$"~{index}"];
            _state.Tokens[key: name] = new TokenState(Origins: origins, EndedBy: null, EndedAt: null);
        }

        /// <summary>Binds an emitter kept in a local as a token on the elements of what it walks (<c>xs[]</c> for
        /// <c>xs.emitter()</c> or <c>xs.where(...)</c>, every argument for <c>zip(a: xs, b: ys)</c>), or on the
        /// statement's temporaries when it walks one.</summary>
        private void BindWalk(string name, Expression walk, int index)
        {
            if (_state == null)
            {
                return;
            }

            HashSet<string>? walked = WalkedContainers(walked: walk) ?? FreeWalkArguments(walk: walk);
            HashSet<string> origins = walked?.Select(selector: w => Join(path: w, suffix: "[]"))
                                            .ToHashSet() ?? [$"~{index}"];
            _state.Tokens[key: name] = new TokenState(Origins: origins, EndedBy: null, EndedAt: null);
        }

        /// <summary>The places a free routine's emitter walks (<c>zip(a: xs, b: ys)</c>): each argument that is a held
        /// place. Null when the head of the chain is no free call, or an argument is a temporary.</summary>
        private HashSet<string>? FreeWalkArguments(Expression walk)
        {
            Expression e = Unwrap(expr: walk);
            while (e is CallExpression { Callee: MemberExpression { Object: var receiver } })
            {
                e = Unwrap(expr: receiver);
            }

            if (e is not CallExpression { Callee: not MemberExpression, ResolvedRoutine: { } callee } free)
            {
                return null;
            }

            var paths = new HashSet<string>(comparer: StringComparer.Ordinal);
            foreach ((_, Expression arg) in BoundArguments(call: free, callee: callee))
            {
                if (owner.PathsOf(expr: arg, tokenOrigins: Held) is not { } argPaths)
                {
                    return null;
                }

                paths.UnionWith(other: argPaths);
            }

            return paths;
        }

        /// <summary>The containers a `for` loop walks, from what its iterator is made of: `xs` for `xs.iter()`, and
        /// for an adapter chain on a held place (`xs.enumerate().iter()`, `xs.view().where(...)`) the place the chain
        /// starts from. Null when the chain starts from anything else (a temporary, a call result), whose source the
        /// builder cannot tell.</summary>
        private HashSet<string>? WalkedContainers(Expression walked)
        {
            Expression e = Unwrap(expr: walked);
            while (e is CallExpression { Callee: MemberExpression { Object: var receiver } } adapter &&
                   !IsTrackedToken(type: adapter.ResolvedType))
            {
                e = Unwrap(expr: receiver);
            }

            if (e is IdentifierExpression { Name: var named } &&
                _iteratorSources.TryGetValue(key: named, value: out HashSet<string>? namedPaths))
            {
                return namedPaths;
            }

            return e is IdentifierExpression or MemberExpression or IndexExpression ||
                   e is CallExpression && IsTrackedToken(type: e.ResolvedType)
                ? owner.PathsOf(expr: e, tokenOrigins: Held)
                : null;
        }

        /// <summary>Whether every container the returned emitter <paramref name="walk"/> walks is reached from
        /// <c>me</c> or from a token parameter: the slots a returned token may come from.</summary>
        private bool WalksReturnSlots(Expression walk)
        {
            HashSet<string>? walked = Unwrap(expr: walk) is IdentifierExpression { Name: var name } &&
                                      _state?.Tokens.GetValueOrDefault(key: name) is { } kept
                ? kept.Origins
                : WalkedContainers(walked: walk);
            HashSet<string> slots = ReturnSlots(routine: routine);
            return walked is { Count: > 0 } &&
                   walked.All(predicate: path => slots.Contains(item: RootName(path: path)));
        }

        private void EndTemporaries(int index, SourceLocation at)
        {
            End(written: $"~{index}", why: "was a temporary, gone at the end of its statement", at: at);
        }

        private void Assign(Expression target, Expression value, int index, SourceLocation at)
        {
            // A local takes the emitter as a token (below), anything else would keep it past what it walks.
            if (_reporting && target is not IdentifierExpression && IsWalkToken(walk: value))
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

            if (target is IdentifierExpression walkName && _state != null && IsWalkToken(walk: value))
            {
                BindWalk(name: walkName.Name, walk: value, index: index);
            }
            else if (target is IdentifierExpression id && _state?.Tokens.ContainsKey(key: id.Name) == true)
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

        /// <summary>RF-S623 for one call handed the same entity twice through token variables that name it
        /// differently (<c>var m2 = m</c>, then <c>both(a: m, b: m2)</c>), with one of the two written: a change through
        /// the write token changes what the other reads. Two arguments spelled as the same place are reported by the
        /// shape check already.</summary>
        private void CheckSameEntityTwice(CallExpression call)
        {
            if (call.ResolvedRoutine is not { } callee || IsRoutineValueCall(call: call))
            {
                return;
            }

            var handed = new List<(string Param, Expression Arg, bool Writes, HashSet<string> Origins)>();
            foreach ((string param, Expression arg) in BoundArguments(call: call, callee: callee))
            {
                TypeSymbol? type = callee.Parameters.FirstOrDefault(predicate: p => p.Name == param)?.Type;
                if (!IsTrackedToken(type: type) || owner.PathsOf(expr: arg, tokenOrigins: Held) is not { } origins)
                {
                    continue;
                }

                handed.Add(item: (param, arg, type!.BareName == RuntimeContract.Modifying, origins));
            }

            for (int a = 0; a < handed.Count; a++)
            {
                for (int b = a + 1; b < handed.Count; b++)
                {
                    (string pa, Expression ea, bool wa, HashSet<string> oa) = handed[index: a];
                    (string pb, Expression eb, bool wb, HashSet<string> ob) = handed[index: b];
                    if (!(wa || wb) ||
                        !oa.Any(predicate: x => ob.Any(predicate: y => x != UnknownOrigin && y != UnknownOrigin &&
                                                                      (UnderOrEqual(path: x, prefix: y) ||
                                                                       UnderOrEqual(path: y, prefix: x)))))
                    {
                        continue;
                    }

                    HashSet<string>? sa = owner.PathsOf(expr: ea, tokenOrigins: _ => null);
                    HashSet<string>? sb = owner.PathsOf(expr: eb, tokenOrigins: _ => null);
                    if (sa != null && sb != null &&
                        sa.Any(predicate: x => sb.Any(predicate: y => UnderOrEqual(path: x, prefix: y) ||
                                                                      UnderOrEqual(path: y, prefix: x))))
                    {
                        continue;
                    }

                    string shared = Shown(path: oa.First(predicate: x => ob.Any(predicate: y =>
                        UnderOrEqual(path: x, prefix: y) || UnderOrEqual(path: y, prefix: x))));
                    owner.Report(code: SemanticDiagnosticCode.SameContainerTwiceInCall,
                        message:
                        $"You are handing '{pa}' and '{pb}' of '{callee.Name}' tokens taken from the same '{shared}', " +
                        "and one of them is written: a change through one changes what the other reads. Pass one " +
                        "token, or tokens on two different entities.",
                        location: call.Location);
                    return;
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
                CheckSameEntityTwice(call: call);
                owner.CheckReadTokenWrites(call: call,
                    isReadElement: e => e is IdentifierExpression { Name: var n } && _readElements.Contains(item: n));
            }

            List<(string Path, string Callee)> writes = owner.CallWrites(call: call, tokenOrigins: Held);
            if (_reporting && inUse.Count > 0)
            {
                // A write the callee makes through its own receiver leaves that receiver alive: the call runs on it.
                Expression? ownReceiver = call.Callee is MemberExpression { Object: var self } ? Unwrap(expr: self) : null;
                HashSet<string> throughReceiver = owner.ReceiverWrites(call: call, tokenOrigins: Held);

                // A `steal` among the arguments is RF-S615 at the stolen variable's other use in the call (the
                // builder's own use-after-steal check says it moves out in the same call), so only the call's own
                // effects are checked here.
                foreach ((string path, string callee) in writes)
                {
                    if (inUse.FirstOrDefault(predicate: u => u.Origin != UnknownOrigin && Ends(written: path, origin: u.Origin) &&
                                                             !(ReferenceEquals(objA: u.Operand, objB: ownReceiver) &&
                                                               throughReceiver.Contains(item: path))) is
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
                                                                    t.Name == "Emittable"))
                                         .Select(selector: c => c.ParameterName)
                                         .ToHashSet(comparer: StringComparer.Ordinal);
        return fields.Any(predicate: f => f.Type.BareName == RuntimeContract.Hijacked &&
                                          f.Type.TypeArguments is [GenericParameterTypeSymbol held] &&
                                          iterableParams.Contains(item: held.Name));
    }

    /// <summary>
    /// Whether <paramref name="walk"/> gives an emitter that points at the container it walks and keeps nothing of
    /// its own: an adapter (<see cref="IsSourceIterable"/>) or an emitter whose fields reach a raw pointer into what
    /// it walks (<c>xs.emitter()</c>, <c>xs.where(...).emitter()</c>). Such an emitter is a token. One built from
    /// <c>steal</c> (<c>(steal xs).emitter()</c>, which takes the elements) holds what it walks, and a range or a
    /// generator points at nothing, so neither is one.
    /// </summary>
    private static bool IsWalkToken(Expression walk)
    {
        if (walk.ResolvedType is not { } type || !(IsSourceIterable(type: type) || PointsIntoSource(type: type, depth: 0)))
        {
            return false;
        }

        Expression e = Unwrap(expr: walk);
        while (true)
        {
            switch (e)
            {
                case StealExpression:
                    return false;
                case CallExpression { ResolvedRoutine.Name: RuntimeContract.TakeEach }:
                    return false;
                case CallExpression { Callee: MemberExpression { Object: var receiver } } call
                    when !IsTrackedToken(type: call.ResolvedType):
                    e = Unwrap(expr: receiver);
                    continue;
                default:
                    return true;
            }
        }
    }

    /// <summary>Whether a value of <paramref name="type"/> is an emitter (it has <c>emit</c>) whose member variables
    /// reach a <c>Hijacked</c> pointer, directly or through the emitter it wraps.</summary>
    private static bool PointsIntoSource(TypeSymbol type, int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        List<MemberVariableInfo>? fields = type switch
        {
            EntityTypeSymbol e when ObeysEmitter(protocols: (e.GenericDefinition as EntityTypeSymbol ?? e).ImplementedProtocols) =>
                e.MemberVariables,
            RecordTypeSymbol r when ObeysEmitter(protocols: (r.GenericDefinition as RecordTypeSymbol ?? r).ImplementedProtocols) =>
                r.MemberVariables,
            _ => null
        };
        // An emitter that writes its own destruct gives back what it holds (`ListTakingEmitter`, the walk of
        // `(steal xs).emitter()`, frees the buffer it took): its pointer is to what it holds, not into a source.
        if (fields != null && HoldsWhatItWalks(type: type))
        {
            return false;
        }

        // An emitter that holds a strong reference to what it walks (the walk `r.emitter()` gives for a
        // `Retained[T]`) keeps it alive as long as the emitter lives: it is valid on its own.
        if (fields != null && fields.Any(predicate: f => f.Type.BareName is RuntimeContract.Retained or
                RuntimeContract.Roamed or RuntimeContract.Guarded))
        {
            return false;
        }

        return fields != null &&
               fields.Any(predicate: f => f.Type.BareName == RuntimeContract.Hijacked ||
                                          PointsIntoSource(type: f.Type, depth: depth + 1));
    }

    /// <summary>Whether <paramref name="type"/> writes its own <c>destruct</c> (one on its definition, not the
    /// builder's).</summary>
    private static bool HoldsWhatItWalks(TypeSymbol type)
    {
        TypeSymbol definition = type switch
        {
            EntityTypeSymbol { GenericDefinition: { } e } => e,
            RecordTypeSymbol { GenericDefinition: { } r } => r,
            _ => type
        };
        return TypeRegistry.Ambient?.GetMemberRoutinesForOwner(ownerType: definition)
                           .Any(predicate: r => r is { Name: RuntimeContract.Destroy, IsSynthesized: false }) == true;
    }

    private static bool ObeysEmitter(IEnumerable<TypeSymbol> protocols)
    {
        return protocols.Any(predicate: p => p.BareName == EmitterProtocolName);
    }

    /// <summary>The protocol an emitter obeys.</summary>
    private const string EmitterProtocolName = "Emitter";

    private void ReportSourceIterableKept(TypeSymbol type, string where, SourceLocation location)
    {
        report(code: SemanticDiagnosticCode.SourceIterableKept,
            message:
            $"You are keeping '{type.BareName}' {where}. It points at the container it walks, so it is a token: a " +
            "local variable may hold it until the container changes shape or ends, and a routine may return one made " +
            "from 'me' or from a token parameter (declare it Viewing[T] or Modifying[T]), but it cannot go anywhere " +
            "else that outlives the container. Collect it with '.to[List]()', build it from 'steal' " +
            "('(steal xs).emitter()') so it holds the elements itself, or keep the container in a 'Retained' and " +
            "take the emitter from the handle ('r.emitter()'), which holds one more reference to it.",
            location: location);
    }

    /// <summary>Whether a token only lets its holder read: <c>Viewing[T]</c>.</summary>
    private static bool IsReadToken(TypeSymbol? type)
    {
        return type?.BareName is RuntimeContract.Viewing;
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
        bool mintsWriteToken = callee.ReturnType?.BareName is RuntimeContract.Modifying;
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
            // A read-token parameter is checked inside the callee: a write through it is reported there. A
            // write-token parameter was reported when the argument was matched to it.
            if (callee.Parameters.FirstOrDefault(predicate: p => p.Name == param) is { } declared &&
                (IsReadToken(type: declared.Type) ||
                 declared.Type?.BareName is RuntimeContract.Modifying))
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
