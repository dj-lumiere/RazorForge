using Builder.Declaration;
using SyntaxTree;
using TypeModel.Symbols;
using TypeModel.Types;

namespace Builder.Lowering.Passes;

/// <summary>
/// Moves the <c>Roamed[E]</c> access-lock bracket out of codegen and into a real AST call. A direct
/// field READ or WRITE through a <c>Roamed[E]</c> handle must be wrapped in
/// <c>handle.lock_enter()</c> … <c>handle.lock_exit()</c> so that — once the object has ESCAPED to
/// another worker — the field touch is serialized. The lock is a no-op while the object is LOCAL and a
/// <b>reentrant</b>, task-keyed acquire once escaped (see <c>Roamed.rf</c>).
///
/// <para>Previously codegen inserted <c>call void @lock_enter/@lock_exit(handle)</c> itself, with NO
/// surface AST call, bracketing each individual field read/write. This pass makes it a REAL AST rewrite
/// instead: for every statement that directly contains a Roamed[E] field access, a
/// <c>handle.lock_enter()</c> <see cref="ExpressionStatement"/> is inserted immediately BEFORE the
/// statement and a matching <c>handle.lock_exit()</c> immediately AFTER it. The read/write itself keeps
/// its value — codegen now just projects through <c>RoamController.data</c> and emits the load/store,
/// with the lock bracket already present as ordinary calls surrounding the whole statement.</para>
///
/// <para><b>Bracket-count decision: per-statement (once per field-access receiver), NOT per-access.</b>
/// The old codegen bracketed each individual access; because the lock is <i>reentrant</i> and a no-op
/// while local, bracketing the whole enclosing statement once per receiver is exact-parity for the
/// escaped-mode serialization semantics (every field touch inside the statement is still inside a held
/// acquire) while keeping this pass simple and safe under NESTED access (multiple Roamed reads in one
/// statement). A single enter…exit spanning the statement cannot leave any field touch unguarded.</para>
///
/// <para>Reachability already seeds <c>lock_enter</c>/<c>lock_exit</c> for every live <c>Roamed[T]</c>
/// via <c>ImplicitCallContract.ForLiveType</c>, so the targets are live/monomorphized. Mirrors
/// <see cref="RoamedSpawnPromotionLoweringPass"/> (statement insertion around a site). Runs after the
/// other Roamed passes so the field accesses it brackets are in final form.</para>
/// </summary>
internal sealed class RoamedLockBracketLoweringPass(PostprocessingContext ctx)
{
    private TypeRegistry Registry => ctx.Registry;

    /// <summary>Inserts Roamed field-access lock brackets across a whole program.</summary>
    public void Run(Program program)
    {
        foreach (SyntaxTree.Declaration decl in
                 program.Declarations.OfType<SyntaxTree.Declaration>())
        {
            LowerDeclaration(decl: decl);
        }
    }

    /// <summary>Inserts Roamed field-access lock brackets in synthesized variant bodies.</summary>
    public void RunOnVariantBodies()
    {
        foreach (string key in ctx.VariantBodies.Keys.ToList())
        {
            LowerBody(body: ctx.VariantBodies[key: key]);
        }
    }

    private void LowerDeclaration(SyntaxTree.Declaration decl)
    {
        switch (decl)
        {
            case RoutineDeclaration r:
                LowerBody(body: r.Body);
                break;
            case EntityDeclaration e:
                LowerMemberList(members: e.Members);
                break;
            case RecordDeclaration rec:
                LowerMemberList(members: rec.Members);
                break;
            case CrashableDeclaration cr:
                LowerMemberList(members: cr.Members);
                break;
        }
    }

    private void LowerMemberList(List<SyntaxTree.Declaration> members)
    {
        foreach (SyntaxTree.Declaration m in members)
        {
            if (m is RoutineDeclaration mr)
            {
                LowerBody(body: mr.Body);
            }
        }
    }

    private void LowerBody(Statement body)
    {
        if (body is BlockStatement block)
        {
            LowerBlock(block: block);
        }
    }

    // ---- Block rewrite (in place, mirroring RoamedSpawnPromotionLoweringPass) ---------------------

    private void LowerBlock(BlockStatement block)
    {
        var rewritten = new List<Statement>(capacity: block.Statements.Count);
        foreach (Statement stmt in block.Statements)
        {
            RecurseInto(stmt: stmt);
            // An atomic-width scalar global RMW (`__globals__.n = __globals__.n.add(d)`) becomes one
            // lock-free AtomicRmwStatement instead of a bracketed load-compute-store. Everything else
            // (heavy-value field RMW, plain read/write) still gets the access-lock brackets.
            if (TryLowerAtomicGlobalRmw(stmt: stmt) is { } atomic)
            {
                rewritten.Add(item: atomic);
                continue;
            }

            List<Expression> handles = FieldAccessHandles(stmt: stmt);
            foreach (Expression h in handles)
            {
                AddBracket(into: rewritten,
                    handle: h,
                    memberRoutine: RuntimeContract.RoamedMemberRoutine.LockEnter);
            }

            rewritten.Add(item: stmt);
            foreach (Expression h in handles)
            {
                AddBracket(into: rewritten,
                    handle: h,
                    memberRoutine: RuntimeContract.RoamedMemberRoutine.LockExit);
            }
        }

        block.Statements.Clear();
        block.Statements.AddRange(collection: rewritten);
    }

    private void AddBracket(List<Statement> into, Expression handle, string memberRoutine)
    {
        if (MakeLockCall(handle: handle, memberRoutine: memberRoutine) is { } call)
        {
            into.Add(item: call);
        }
    }

    // ---- Lock-free atomic global RMW ----------------------------------------------------------------

    /// <summary>
    /// Recognizes an atomic-width scalar module-<c>global</c> read-modify-write, which
    /// <c>GlobalEntityRewritePass</c> has turned into a field update on the hidden <c>__ModuleGlobals</c>
    /// singleton (<c>__globals__.g = __globals__.g.add(d)</c> or <c>.sub(d)</c>), and returns the
    /// lock-free <see cref="AtomicRmwStatement"/> that replaces it, or null when the statement doesn't
    /// match. It matches only when the field is an atomic width (S8..S64, U8..U64, B32, B64) and the
    /// delta reads no other module-global field (an unlocked read of a heavy field could tear). Heavy
    /// fields (S128/S256/B16/B128/Text/Decimal/records) keep the locked path. Atomic arithmetic wraps on
    /// overflow instead of crashing like a checked add, as atomics do everywhere.
    /// </summary>
    private static AtomicRmwStatement? TryLowerAtomicGlobalRmw(Statement stmt)
    {
        (Expression Target, Expression Value)? tv = stmt switch
        {
            AssignmentStatement a => (a.Target, a.Value),
            ExpressionStatement
            {
                Expression: BinaryExpression { Operator: BinaryOperator.Assign } b
            } => (b.Left, b.Right),
            _ => null
        };
        if (tv is not { Target: MemberExpression tm } p)
        {
            return null;
        }

        if (ModuleGlobalsInnerEntity(t: tm.Object.ResolvedType) is null ||
            !IsAtomicWidthScalar(t: tm.ResolvedType))
        {
            return null;
        }

        Expression v = p.Value is NamedArgumentExpression nav
            ? nav.Value
            : p.Value;
        if (v is not CallExpression
            {
                Callee: MemberExpression { Object: MemberExpression vm, MemberName: var op },
                Arguments: [{ } deltaArg]
            })
        {
            return null;
        }

        AtomicRmwOperation? operation = op switch
        {
            "add" => AtomicRmwOperation.Add,
            "sub" => AtomicRmwOperation.Subtract,
            _ => null
        };
        if (operation is null || !IsSameSingletonField(tm: tm, vm: vm))
        {
            return null;
        }

        Expression delta = deltaArg is NamedArgumentExpression nad
            ? nad.Value
            : deltaArg;
        if (ReferencesModuleGlobalField(e: delta))
        {
            return null;
        }

        return new AtomicRmwStatement(Field: tm,
            Operation: operation.Value,
            Delta: delta,
            Location: stmt.Location);
    }

    /// <summary>True when both member expressions name the same field on the same <c>__globals__</c>
    /// singleton identifier.</summary>
    private static bool IsSameSingletonField(MemberExpression tm, MemberExpression vm)
    {
        return tm.MemberName == vm.MemberName && tm.Object is IdentifierExpression ti &&
               vm.Object is IdentifierExpression vi && ti.Name == vi.Name;
    }

    /// <summary>The <c>__ModuleGlobals</c> entity inside a <c>Roamed[__ModuleGlobals]</c> handle type,
    /// or null when the type is not that handle.</summary>
    private static EntityTypeSymbol? ModuleGlobalsInnerEntity(TypeSymbol? t)
    {
        EntityTypeSymbol? inner = t switch
        {
            WrapperTypeSymbol { Name: RuntimeContract.Roamed, InnerType: EntityTypeSymbol e } => e,
            RecordTypeSymbol
            {
                GenericDefinition.Name: RuntimeContract.Roamed,
                TypeArguments: [EntityTypeSymbol e]
            } => e,
            _ => null
        };
        return inner?.BareName == Builder.Desugaring.Passes.ModuleGlobalsSynthesisPass.ModuleGlobalsEntityName
            ? inner
            : null;
    }

    private static bool IsAtomicWidthScalar(TypeSymbol? t)
    {
        return t?.BareName is "S8" or "S16" or "S32" or "S64" or "U8" or "U16" or "U32" or "U64" or "B32"
            or "B64";
    }

    private static bool ReferencesModuleGlobalField(Expression e)
    {
        bool found = false;
        AstWalker.WalkExpressions(root: e,
            visit: n =>
            {
                if (n is MemberExpression m &&
                    ModuleGlobalsInnerEntity(t: m.Object.ResolvedType) is not null)
                {
                    found = true;
                }
            });
        return found;
    }

    // ---- Field-access handles -----------------------------------------------------------------------

    // The Roamed[E] handle expressions of every DIRECT field access (read or write) in `stmt`'s own
    // expressions. One entry per field-access occurrence (per-statement bracketing, see class doc).
    // Nested statements are bracketed when the block recursion reaches them, so only this statement's
    // own expressions are walked here (matching RoamedSpawnPromotionLoweringPass.CollectPromotes).
    private static List<Expression> FieldAccessHandles(Statement stmt)
    {
        var handles = new List<Expression>();
        foreach (Expression e in DirectExpressions(stmt: stmt))
        {
            AstWalker.WalkExpressions(root: e,
                visit: n =>
                {
                    if (n is not MemberExpression member)
                    {
                        return;
                    }

                    // A direct field access through a Roamed handle, OR a memberRoutine-dispatch deref (the
                    // `control`/`refer`/`raw_inner` coercion RoamedProjectionLoweringPass inserts on a
                    // Roamed receiver). Both must hold the access lock across the enclosing statement —
                    // otherwise a memberRoutine call on a Roamed receiver (e.g. an SF wrapper's `xs.getitem!(i)`,
                    // whose bare-`me` inner is reached via the coercion) touches the object UNLOCKED,
                    // breaking escaped-mode serialization. Coarse (whole-statement) bracketing is exact for
                    // the reentrant, task-keyed lock.
                    if (RoamedFieldReceiver(member: member) is { } fieldHandle)
                    {
                        handles.Add(item: fieldHandle);
                    }
                    else if (RoamedCoercionReceiver(member: member) is { } coerceHandle)
                    {
                        handles.Add(item: coerceHandle);
                    }
                });
        }

        return handles;
    }

    // The expressions directly owned by a statement (its condition/value/target), excluding nested
    // Statement bodies — those are visited separately by the block recursion.
    private static IEnumerable<Expression> DirectExpressions(Statement stmt)
    {
        switch (stmt)
        {
            case ExpressionStatement es:
                yield return es.Expression;
                break;
            case DiscardStatement ds:
                yield return ds.Expression;
                break;
            case ReturnStatement { Value: not null } s: yield return s.Value; break;
            case VariantReturnStatement { Value: not null } s: yield return s.Value; break;
            case BecomesStatement s: yield return s.Value; break;
            case ThrowStatement s: yield return s.Error; break;
            case AssignmentStatement s:
                yield return s.Target;
                yield return s.Value;
                break;
            case DestructuringStatement s: yield return s.Initializer; break;
            case DeclarationStatement
            {
                Declaration: VariableDeclaration { Initializer: not null } v
            }:
                yield return v.Initializer; break;
            case IfStatement s: yield return s.Condition; break;
            case WhileStatement s: yield return s.Condition; break;
            case EachStatement s: yield return s.Iterable; break;
            case WhenStatement s: yield return s.Expression; break;
            case UsingStatement s: yield return s.Resource; break;
        }
    }

    // When `member` is a DIRECT field access through a Roamed[E] handle (the member name is a member
    // VARIABLE of the inner entity, not a memberRoutine — a memberRoutine call's callee member name never matches an
    // entity field), returns the Roamed[E] handle expression to bracket; otherwise null.
    private static Expression? RoamedFieldReceiver(MemberExpression member)
    {
        if (RoamedInnerEntity(t: member.Object.ResolvedType) is not { } innerEntity)
        {
            return null;
        }

        bool isField =
            innerEntity.MemberVariables.Any(predicate: mv => mv.Name == member.MemberName);
        return isField
            ? member.Object
            : null;
    }

    // When `member` is a deref COERCION (`control` / `refer` / `raw_inner`) on a Roamed[E] handle —
    // the receiver projection RoamedProjectionLoweringPass inserts for a memberRoutine call on a Roamed
    // receiver — returns the Roamed[E] handle to bracket; otherwise null. This is what makes a memberRoutine
    // call on a Roamed receiver hold the access lock (the field-access path above only catches direct
    // field touches). The lifecycle coercions on the handle itself (`roam`/`promote`/`lock_*`/`destroy`)
    // are NOT deref and are deliberately excluded.
    private static Expression? RoamedCoercionReceiver(MemberExpression member)
    {
        if (member.MemberName is not (RuntimeContract.RoamedMemberRoutine.RawInner
            or RuntimeContract.Control or RuntimeContract.Access))
        {
            return null;
        }

        return RoamedInnerEntity(t: member.Object.ResolvedType) is not null
            ? member.Object
            : null;
    }

    // The bare entity `E` inside a `Roamed[E]` handle, in either representation the pipeline produces
    // (WrapperTypeSymbol from SuflaeEntityLoweringPass.WrapInRoam, RecordTypeSymbol from a resolver-built
    // handle). Null when the type is not a Roamed handle over an entity.
    private static EntityTypeSymbol? RoamedInnerEntity(TypeSymbol? t)
    {
        return t switch
        {
            WrapperTypeSymbol { Name: RuntimeContract.Roamed, InnerType: EntityTypeSymbol e } => e,
            RecordTypeSymbol
            {
                GenericDefinition.Name: RuntimeContract.Roamed,
                TypeArguments: [EntityTypeSymbol e]
            } => e,
            _ => null
        };
    }

    // Build `handle.lock_enter()` / `handle.lock_exit()` as an ExpressionStatement. Both are dangerous,
    // take the Roamed handle, and return void — so the statement is a pure side effect around the field
    // access, exactly what the removed codegen bracket did. The handle node is reused (side-effect-free
    // to re-evaluate: an identifier / member handle), mirroring the promote/retain steps.
    private ExpressionStatement? MakeLockCall(Expression handle, string memberRoutine)
    {
        if (handle.ResolvedType is not { } recvType)
        {
            return null;
        }

        RoutineInfo? routine =
            Registry.LookupMemberRoutine(type: recvType, memberRoutineName: memberRoutine);
        if (routine is null)
        {
            return null;
        }

        var callee =
            new MemberExpression(Object: handle,
                MemberName: memberRoutine,
                Location: handle.Location) { ResolvedType = recvType };
        var call =
            new CallExpression(Callee: callee,
                Arguments: new List<Expression>(),
                Location: handle.Location)
            {
                ResolvedRoutine = routine,
                ResolvedType = routine.ReturnType,
                LoweringKind =
                    Verification.CallClassifier.ClassifyMemberRoutineCall(memberRoutine: routine)
            };
        return new ExpressionStatement(Expression: call, Location: handle.Location);
    }

    // ---- Nested-block recursion (mirrors RoamedSpawnPromotionLoweringPass) ------------------------

    private void RecurseInto(Statement stmt)
    {
        switch (stmt)
        {
            case BlockStatement b:
                LowerBlock(block: b);
                break;
            case IfStatement i:
                RecurseStmt(stmt: i.ThenStatement);
                if (i.ElseStatement != null)
                {
                    RecurseStmt(stmt: i.ElseStatement);
                }

                break;
            case WhileStatement w:
                RecurseStmt(stmt: w.Body);
                if (w.ElseBranch != null)
                {
                    RecurseStmt(stmt: w.ElseBranch);
                }

                break;
            case LoopStatement l:
                RecurseStmt(stmt: l.Body);
                break;
            case EachStatement f:
                RecurseStmt(stmt: f.Body);
                if (f.ElseBranch != null)
                {
                    RecurseStmt(stmt: f.ElseBranch);
                }

                break;
            case DangerStatement d:
                LowerBlock(block: d.Body);
                break;
            case UsingStatement u:
                RecurseStmt(stmt: u.Body);
                if (u.FallbackBody != null)
                {
                    RecurseStmt(stmt: u.FallbackBody);
                }

                break;
            case WhenStatement whenStmt:
                foreach (WhenClause clause in whenStmt.Clauses)
                {
                    RecurseStmt(stmt: clause.Body);
                }

                break;
        }
    }

    private void RecurseStmt(Statement stmt)
    {
        if (stmt is BlockStatement b)
        {
            LowerBlock(block: b);
        }
        else
        {
            RecurseInto(stmt: stmt);
        }
    }
}
