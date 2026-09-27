using Builder.Diagnostics;
using Builder.Tokenizer;
using SyntaxTree;

namespace Builder.Desugaring.Passes;

/// <summary>
/// Suflae <c>global</c> storage synthesis, run on the user programs at the start of semantic analysis so
/// every entry point (build, check, codegen, tests) sees the same program shape.
/// </summary>
internal static class ModuleGlobalsSynthesisPass
{
    /// <summary>Name of the synthesized per-program entity that holds every Suflae module-level
    /// <c>global</c> as a field. Routing all globals through ONE <c>entity</c> behind a hidden
    /// <c>Roamed</c> singleton makes them thread-safe on the M:N worker pool: the per-statement
    /// access-lock brackets (RoamedLockBracketLoweringPass) serialize concurrent field RMW, and
    /// atomic-width scalar fields additionally get a lock-free <c>atomicrmw</c> fast-path on the field
    /// address. The singleton is constructed and promoted at the top of <c>start()</c>.</summary>
    internal const string ModuleGlobalsEntityName = "__ModuleGlobals";

    /// <summary>Name of the hidden singleton holding the one <see cref="ModuleGlobalsEntityName"/>
    /// instance. Every module-level global access <c>g</c> is rewritten to <c>__globals__.g</c> by
    /// GlobalEntityRewritePass.</summary>
    internal const string ModuleGlobalsSingletonName = "__globals__";

    /// <summary>
    /// Suflae <c>global</c> eager initialization + thread-safe storage synthesis. Each module-level
    /// <c>global name: T = init</c> becomes a FIELD of one hidden per-program entity
    /// <see cref="ModuleGlobalsEntityName"/>, stored behind a single <c>Roamed</c> singleton
    /// <see cref="ModuleGlobalsSingletonName"/> (constructed + promoted at the top of <c>start()</c>).
    /// Field initializers run in dependency order (a global's init may read another global; a cycle —
    /// including self-reference, including through a free-routine call — is RF-S436). Initializers that
    /// touch no other global are folded straight into the singleton constructor; a dependent initializer
    /// runs as an ordered field assignment AFTER construction so its read sees the already-initialized
    /// field. The original <c>global</c> declarations are kept (init stripped) ONLY so semantic analysis
    /// registers them and stamps <c>IdentifierExpression.IsModuleGlobal</c> on every reference;
    /// GlobalEntityRewritePass then deletes them and rewrites each stamped reference to
    /// <c>__globals__.field</c>.
    /// Returns false after reporting through <paramref name="report"/> when the globals cannot be
    /// synthesized (RF-S436 / RF-S437 / RF-S438).
    /// </summary>
    internal static bool Run(List<(SyntaxTree.Program Program, string FilePath)> orderedFiles,
        Action<SemanticDiagnosticCode, string, SourceLocation> report)
    {
        // 1) Collect globals (in encounter order) and strip their initializers off the declarations. The
        //    (now init-less) declaration is kept so SA still registers the global for reference resolution.
        var collected =
            new List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)>();
        foreach ((SyntaxTree.Program program, string _) in orderedFiles)
        {
            List<ISyntaxTreeNode> decls = program.Declarations;
            for (int i = 0; i < decls.Count; i++)
            {
                if (decls[index: i] is VariableDeclaration
                    {
                        IsGlobal: true, Initializer: not null, Type: not null
                    } g)
                {
                    collected.Add(item: (g.Name, g.Type, g.Initializer, g.Location));
                    decls[index: i] = g with { Initializer = null };
                }
            }
        }

        if (collected.Count == 0)
        {
            return true;
        }

        List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)> globals =
            DeduplicateGlobals(collected: collected);
        int n = globals.Count;
        List<HashSet<int>> deps =
            ComputeGlobalDependencies(orderedFiles: orderedFiles, globals: globals);
        List<int> order = KahnOrder(deps: deps, n: n);

        if (order.Count != n)
        {
            List<int> cyclic = Enumerable.Range(start: 0, count: n)
                                         .Where(predicate: i => !order.Contains(value: i))
                                         .ToList();
            string names = string.Join(separator: ", ",
                values: cyclic.Select(selector: i => $"'{globals[index: i].Name}'"));
            report(SemanticDiagnosticCode.ModuleGlobalCircularInit,
                $"The globals {names} read each other in their initializers, so none of them can be " +
                "initialized first. Give one of them an initializer that does not read the others.",
                globals[index: cyclic[index: 0]].Loc);
            return false;
        }

        if (!TryBuildModuleGlobalsSynthesis(globals: globals,
                deps: deps,
                order: order,
                report: report,
                entityDecl: out EntityDeclaration entityDecl,
                singletonDecl: out VariableDeclaration singletonDecl,
                initStmts: out List<Statement> initStmts))
        {
            return false;
        }

        return SpliceGlobalsIntoStart(orderedFiles: orderedFiles,
            report: report,
            entityDecl: entityDecl,
            singletonDecl: singletonDecl,
            initStmts: initStmts);
    }

    /// <summary>Deduplicates collected globals by name (last-write-wins) while preserving first-seen order
    /// for a stable field layout.</summary>
    private static List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)>
        DeduplicateGlobals(
            List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)>
                collected)
    {
        var seenOrder = new List<string>();
        var latest =
            new Dictionary<string, (TypeExpression Type, Expression Init, SourceLocation Loc)>(
                comparer: StringComparer.Ordinal);
        foreach ((string name, TypeExpression type, Expression init, SourceLocation loc) in
                 collected)
        {
            if (!latest.ContainsKey(key: name))
            {
                seenOrder.Add(item: name);
            }

            latest[key: name] = (type, init, loc);
        }

        return seenOrder.Select(selector: name => (Name: name, latest[key: name].Type,
                             latest[key: name].Init, latest[key: name].Loc))
                        .ToList();
    }

    /// <summary>Builds the synthesized entity declaration, singleton declaration, and the init-statement
    /// list that gets prepended to <c>start()</c>. Returns false (after reporting RF-S437) if a dependent
    /// field has a type with no synthesizable default.</summary>
    private static bool TryBuildModuleGlobalsSynthesis(
        List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)> globals,
        List<HashSet<int>> deps, List<int> order,
        Action<SemanticDiagnosticCode, string, SourceLocation> report, out EntityDeclaration entityDecl,
        out VariableDeclaration singletonDecl, out List<Statement> initStmts)
    {
        int n = globals.Count;
        SourceLocation loc0 = globals[index: 0].Loc;

        // 3) Build the __ModuleGlobals entity — one field per global (`name: Type`, no initializer).
        var fieldDecls = new List<SyntaxTree.Declaration>(capacity: n);
        for (int i = 0; i < n; i++)
        {
            fieldDecls.Add(item: new VariableDeclaration(Name: globals[index: i].Name,
                Type: globals[index: i].Type,
                Initializer: null,
                Visibility: VisibilityModifier.Open,
                Location: globals[index: i].Loc));
        }

        entityDecl = new EntityDeclaration(Name: ModuleGlobalsEntityName,
            GenericParameters: null,
            Protocols: new List<TypeExpression>(),
            Members: fieldDecls,
            Visibility: VisibilityModifier.Open,
            Location: loc0);

        // 4) Constructor arguments: independent fields use their real initializer; dependent fields
        //    are seeded with a type default and get their real value from a post-construction assignment.
        var ctorArgs = new List<(string Name, Expression Value)>(capacity: n);
        for (int i = 0; i < n; i++)
        {
            Expression value;
            if (deps[index: i].Count == 0)
            {
                value = globals[index: i].Init;
            }
            else
            {
                LiteralExpression? def = DefaultInitializerFor(type: globals[index: i].Type,
                    loc: globals[index: i].Loc);
                if (def == null)
                {
                    report(SemanticDiagnosticCode.ModuleGlobalDependentInitUnsupported,
                        $"The global '{globals[index: i].Name}: {globals[index: i].Type.Name}' has an " +
                        "initializer that reads another global, which only works for number, Text and Bool " +
                        "globals. Initialize it without reading other globals.",
                        globals[index: i].Loc);
                    singletonDecl = null!;
                    initStmts = null!;
                    return false;
                }

                value = def;
            }

            ctorArgs.Add(item: (globals[index: i].Name, value));
        }

        var construct = new CreatorExpression(TypeName: ModuleGlobalsEntityName,
            TypeArguments: null,
            MemberVariables: ctorArgs,
            Location: loc0);

        // 5) Init statements: construct + promote singleton, then ordered assignments for dependent globals.
        initStmts = new List<Statement>(capacity: n + 1)
        {
            new AssignmentStatement(
                Target: new IdentifierExpression(Name: ModuleGlobalsSingletonName,
                    Location: loc0),
                Value: construct,
                Location: loc0) { IsGlobalInit = true }
        };
        foreach (int i in order)
        {
            if (deps[index: i].Count == 0)
            {
                continue; // independent — already set by the constructor
            }

            initStmts.Add(item: new AssignmentStatement(
                Target: new IdentifierExpression(Name: globals[index: i].Name,
                    Location: globals[index: i].Loc),
                Value: globals[index: i].Init,
                Location: globals[index: i].Loc));
        }

        // 6) The singleton declaration — an entity `global` stored behind a promoted Roamed[E] handle.
        singletonDecl = new VariableDeclaration(Name: ModuleGlobalsSingletonName,
            Type: new TypeExpression(Name: ModuleGlobalsEntityName,
                GenericArguments: null,
                Location: loc0),
            Initializer: null,
            Visibility: VisibilityModifier.Open,
            Location: loc0,
            IsGlobal: true);
        return true;
    }

    /// <summary>Splices the synthesized entity/singleton declarations and init statements into the first
    /// program that contains <c>routine start()</c>. Returns false (after reporting RF-S438) if none is
    /// found.</summary>
    private static bool SpliceGlobalsIntoStart(
        List<(SyntaxTree.Program Program, string FilePath)> orderedFiles,
        Action<SemanticDiagnosticCode, string, SourceLocation> report, EntityDeclaration entityDecl, VariableDeclaration singletonDecl, List<Statement> initStmts)
    {
        foreach ((SyntaxTree.Program program, string _) in orderedFiles)
        {
            BlockStatement? startBlock = null;
            foreach (ISyntaxTreeNode node in program.Declarations)
            {
                if (node is RoutineDeclaration { Name: "start", Body: BlockStatement block })
                {
                    startBlock = block;
                    break;
                }
            }

            if (startBlock == null)
            {
                continue;
            }

            // Append (NOT prepend) the synthesized declarations: imports must stay at the top of the
            // file (RF-S114). Declaration order does not matter for type collection.
            program.Declarations.Add(item: entityDecl);
            program.Declarations.Add(item: singletonDecl);
            startBlock.Statements.InsertRange(index: 0, collection: initStmts);
            return true;
        }

        report(SemanticDiagnosticCode.ModuleGlobalWithoutStart,
            "Your globals are initialized when the program starts, but no file in the build has a " +
            "'routine start()' or top-level statements. Add one to the program's entry file.",
            singletonDecl.Location);
        return false;
    }

    /// <summary>Computes, for each global, the set of OTHER globals it (transitively) depends on. A global's
    /// initializer reading another global is a dependency; a free-routine call is followed once into the
    /// callee's body so a hidden read (`global a = compute()` where `compute` reads `b`) counts too.
    /// (Member-routine calls are not followed — a global read hidden behind `x.foo()` is the residual.)</summary>
    private static List<HashSet<int>> ComputeGlobalDependencies(
        List<(SyntaxTree.Program Program, string FilePath)> orderedFiles,
        List<(string Name, TypeExpression Type, Expression Init, SourceLocation Loc)> globals)
    {
        int n = globals.Count;
        var nameToIdx = new Dictionary<string, int>(comparer: StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            nameToIdx[key: globals[index: i].Name] = i; // last decl of a dup name wins
        }

        // Index every free routine's body by bare name so the dependency scan can follow calls.
        var routineBodies = new Dictionary<string, Statement>(comparer: StringComparer.Ordinal);
        foreach ((SyntaxTree.Program program, string _) in orderedFiles)
        {
            foreach (ISyntaxTreeNode node in program.Declarations)
            {
                if (node is RoutineDeclaration { Body: { } body } r)
                {
                    routineBodies[key: r.Name] = body;
                }
            }
        }

        var deps = new List<HashSet<int>>(capacity: n);
        for (int i = 0; i < n; i++)
        {
            deps.Add(item: ComputeSingleGlobalDeps(init: globals[index: i].Init,
                nameToIdx: nameToIdx,
                routineBodies: routineBodies));
        }

        return deps;
    }

    /// <summary>BFS over AST expressions from the given initializer; collects indices of globals this
    /// global directly or transitively depends on by following free-routine call bodies one level.</summary>
    private static HashSet<int> ComputeSingleGlobalDeps(Expression init,
        Dictionary<string, int> nameToIdx, Dictionary<string, Statement> routineBodies)
    {
        var d = new HashSet<int>();
        var visitedRoutines = new HashSet<string>(comparer: StringComparer.Ordinal);
        var toScan = new Queue<object>();
        toScan.Enqueue(item: init);
        while (toScan.Count > 0)
        {
            object root = toScan.Dequeue();
            AstWalker.WalkExpressions(root: root,
                visit: e =>
                {
                    if (e is IdentifierExpression id &&
                        nameToIdx.TryGetValue(key: id.Name, value: out int j))
                    {
                        d.Add(item: j);
                    }

                    // Follow a call into the callee's body once (transitive hidden dependency).
                    if (e is CallExpression { Callee: IdentifierExpression callee } &&
                        routineBodies.TryGetValue(key: callee.Name,
                            value: out Statement? calleeBody) &&
                        visitedRoutines.Add(item: callee.Name))
                    {
                        toScan.Enqueue(item: calleeBody);
                    }
                });
        }

        return d;
    }

    /// <summary>Kahn's topological sort over the dependency edges (dependency j before dependent i), stable
    /// in source order among ready nodes. A returned order shorter than <paramref name="n"/> signals a cycle
    /// (the caller reports the un-ordered globals as RF-S436).</summary>
    private static List<int> KahnOrder(List<HashSet<int>> deps, int n)
    {
        int[] indegree = new int[n];
        for (int i = 0; i < n; i++)
        {
            indegree[i] += deps[index: i]
               .Count(predicate: j => j != i); // edge j -> i (dependency j before dependent i)
        }

        var order = new List<int>(capacity: n);
        bool ready;
        do
        {
            ready = KahnSweep(deps: deps,
                indegree: indegree,
                n: n,
                order: order);
        } while (ready);

        return order;
    }

    /// <summary>Single Kahn sweep: enqueues all zero-indegree nodes into <paramref name="order"/> and
    /// decrements their dependents' indegrees. Returns true if at least one node was emitted.</summary>
    private static bool KahnSweep(List<HashSet<int>> deps, int[] indegree, int n,
        List<int> order)
    {
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            if (indegree[i] != 0)
            {
                continue;
            }

            indegree[i] = -1; // consumed
            order.Add(item: i);
            any = true;
            for (int k = 0; k < n; k++)
            {
                if (k != i && deps[index: k]
                       .Contains(item: i))
                {
                    indegree[k]--;
                }
            }
        }

        return any;
    }

    /// <summary>A build-time default value for a <c>global</c> whose initializer depends on another
    /// global (so its real value is assigned after the singleton is constructed). A bare integer literal
    /// <c>0</c> conforms to any numeric type (int/float/decimal) via RF-S767; Text and Bool have their
    /// own empty/false defaults. Returns null for a type with no synthesizable default.</summary>
    private static LiteralExpression? DefaultInitializerFor(TypeExpression type,
        SourceLocation loc)
    {
        return type.Name switch
        {
            "Text" => new LiteralExpression(Value: "",
                LiteralType: TokenType.TextLiteral,
                Location: loc),
            "Bool" => new LiteralExpression(Value: false,
                LiteralType: TokenType.False,
                Location: loc),
            "S8" or "S16" or "S32" or "S64" or "S128" or "S256" or "U8" or "U16" or "U32" or "U64"
                or "U128" or "U256" or "B16" or "B32" or "B64" or "B128" or "F256" or "Decimal"
                or "D32" or "D64" or "D128" or "Integer" => new LiteralExpression(Value: "0",
                    LiteralType: TokenType.UndecidedInteger,
                    Location: loc),
            _ => null
        };
    }
}
