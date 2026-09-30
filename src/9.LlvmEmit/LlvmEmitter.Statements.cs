using System.Text;
using SyntaxTree;
using TypeModel.Symbols;
using TypeModel.Types;

namespace Builder.LlvmEmit;

/// <summary>
/// Statement code generation: control flow, assignments, declarations, returns.
/// </summary>
public partial class LlvmEmitter
{
    #region Statement Dispatch

    /// <summary>
    /// Main statement dispatch - generates code for any statement type.
    /// Returns true if the statement is a terminator (return, break, continue, throw).
    /// </summary>
    /// <param name="sb">StringBuilder to emit code to.</param>
    /// <param name="stmt">The statement to generate code for.</param>
    /// <returns>True if the statement terminates the current block.</returns>
    private bool EmitStatement(StringBuilder sb, Statement stmt)
    {
        SourceLocation? savedLoc = PushDebugLoc(sb: sb, loc: stmt.Location);
        try
        {
            switch (stmt)
            {
                case BlockStatement block:
                    return EmitBlock(sb: sb, block: block);

                case ExpressionStatement exprStmt:
                    EmitExpression(sb: sb, expr: exprStmt.Expression);
                    return false;

                case DeclarationStatement decl:
                    EmitDeclarationStatement(sb: sb, decl: decl);
                    return false;

                case AssignmentStatement assign:
                    EmitAssignment(sb: sb, assign: assign);
                    return false;

                case ReturnStatement ret:
                    EmitReturn(sb: sb, ret: ret);
                    return true; // Return terminates the block

                case IfStatement ifStmt:
                    return EmitIf(sb: sb, ifStmt: ifStmt);

                case LoopStatement loopStmt:
                    EmitLoop(sb: sb, loopStmt: loopStmt);
                    return false;

                case BreakStatement:
                    EmitBreak(sb: sb);
                    return true; // Break terminates the block

                case ContinueStatement:
                    EmitContinue(sb: sb);
                    return true; // Continue terminates the block

                case PassStatement:
                    // No-op, nothing to emit
                    return false;

                case DangerStatement danger:
                    // danger block - just emit the body
                    return EmitBlock(sb: sb, block: danger.Body);

                case WhenStatement whenStmt:
                    return EmitWhen(sb: sb, whenStmt: whenStmt);

                case AtomicRmwStatement atomic:
                    EmitAtomicRmw(sb: sb, atomic: atomic);
                    return false;

                case DiscardStatement discard:
                    // Note: creator expressions could skip evaluation entirely (creators have no observable
                    // side effects and their result is being discarded, so the allocation is wasted) — not yet implemented.
                    EmitExpression(sb: sb, expr: discard.Expression);
                    return false;

                case UsingStatement:
                    throw new InvalidOperationException(
                        message:
                        "UsingStatement reached codegen -> UsingLoweringPass must run before codegen.");

                case ThrowStatement throwStmt:
                    EmitThrow(sb: sb, throwStmt: throwStmt);
                    return true; // Throw terminates the block

                case AbsentStatement absentStmt:
                    EmitAbsent(sb: sb, absentStmt: absentStmt);
                    return true; // Absent terminates the block

                case VariantReturnStatement variantRet:
                    throw new InvalidOperationException(
                        message:
                        $"VariantReturnStatement ({variantRet.VariantKind}/{variantRet.SiteKind}) reached codegen " +
                        $"in routine [{_currentRoutineDiagName}] (ret={_currentRoutineReturnType?.FullName ?? "null"}) " +
                        "— VariantReturnLoweringPass must lower all carrier returns to record construction.");

                default:
                    throw new NotImplementedException(
                        message: $"Statement type not implemented: {stmt.GetType().Name}");
            }
        }
        finally
        {
            PopDebugLoc(sb: sb, prev: savedLoc);
        }
    }

    /// <summary>
    /// Emits all statements in a block.
    /// Returns true if the block terminates (any statement is a terminator).
    /// </summary>
    private bool EmitBlock(StringBuilder sb, BlockStatement block)
    {
        return block.Statements.Any(predicate: stmt => EmitStatement(sb: sb, stmt: stmt));
    }

    #endregion

    #region Variable Declarations

    /// <summary>
    /// Emits code for a declaration statement.
    /// Handles variable declarations with alloca + store.
    /// </summary>
    private void EmitDeclarationStatement(StringBuilder sb, DeclarationStatement decl)
    {
        if (decl.Declaration is VariableDeclaration varDecl)
        {
            EmitVariableDeclaration(sb: sb, varDecl: varDecl);
        }
        // Other declaration types (function, type) are handled at module level
    }

    /// <summary>
    /// Emits code for a variable declaration.
    /// Creates stack allocation and optionally stores initial value.
    /// </summary>
    private void EmitVariableDeclaration(StringBuilder sb, VariableDeclaration varDecl)
    {
        // Determine the type
        TypeSymbol? varType = ResolveVariableDeclType(varDecl: varDecl) ??
                            throw UndeterminableVariableType(varDecl: varDecl);

        string llvmType = GetValueLlvmType(type: varType);

        // Generate unique LLVM name for this variable (handles shadowing/redeclaration)
        string uniqueName = NextUniqueLocalName(name: varDecl.Name);
        string varPtr = $"%{uniqueName}.addr";
        EmitEntryAlloca(llvmName: varPtr,
            llvmType: llvmType,
            align: ForcedAllocaAlignment(type: varType));

        // Register local variable for identifier lookup
        _localVariables[key: varDecl.Name] = varType;
        _localVarLlvmNames[key: varDecl.Name] = uniqueName;

        ZeroInitOwnedLocalSlot(varDecl: varDecl, varType: varType, varPtr: varPtr);

        // Store initial value if present
        if (varDecl.Initializer == null)
        {
            EmitLateInitPlaceholder(sb: sb,
                varDecl: varDecl,
                varType: varType,
                llvmType: llvmType,
                varPtr: varPtr);
            return;
        }

        string value = EmitExpression(sb: sb, expr: varDecl.Initializer);

        // None initializer: the expression ran for its side effects but produces no value — `void`
        // carries nothing. Store the unit `{}` into the {} alloca.
        if (GetLlvmType(type: varType) == "void")
        {
            EmitLine(sb: sb, line: $"  store {{}} zeroinitializer, ptr {varPtr}");
            return;
        }

        value = CoerceInitializerToDeclaredType(sb: sb,
            varDecl: varDecl,
            varType: varType,
            llvmType: llvmType,
            value: value);
        EmitLine(sb: sb, line: $"  store {llvmType} {value}, ptr {varPtr}");

        // NOTE: the per-RC-field retain on an initial RC-field-record copy is now an explicit AST call
        // inserted by RcRetainLoweringPass (Phase 8) — codegen no longer bumps refcounts itself.

        // NOTE: no codegen strong-count bump for RC wrapper var bindings. Copying a Retained[T]/
        // Tracked[T] handle requires an explicit verb (`.retain()`/`.track()`) — implicit copy
        // (`var b = a`) is a COMPILE ERROR (ImplicitWrapperCopy; Retained/Tracked don't obey
        // Assignable). So an init is always either a fresh handle from `.retain()`/`.track()`
        // (already count=1) or a creator `Retained[T](ctrl)` (count=1) — never an implicit copy
        // needing balance. The old bump (fired on `is not CallExpression`) wrongly counted the
        // teardown return-spill `var __td_ret = Retained[T](ctrl)` (a CreatorExpression) as a copy,
        // injecting a spurious retain → strong 1→2 → double-free at scope exit. Removed.

        NullStampStolenLocal(sb: sb, expr: varDecl.Initializer);
    }

    /// <summary>Builds the diagnostic thrown when a variable's type cannot be determined.</summary>
    private static InvalidOperationException UndeterminableVariableType(
        VariableDeclaration varDecl)
    {
        string typeText = "<null>";
        if (varDecl.Type != null)
        {
            typeText = varDecl.Type.Name;
            if (varDecl.Type.GenericArguments is { Count: > 0 } args)
            {
                typeText +=
                    $"[{string.Join(separator: ", ", values: args.Select(selector: a => a.Name))}]";
            }
        }

        string initializerText = varDecl.Initializer?.GetType()
                                        .Name ?? "<null>";
        return new InvalidOperationException(
            message:
            $"Cannot determine type for variable '{varDecl.Name}' (declared type: {typeText}, initializer: {initializerText})");
    }

    /// <summary>Generates a unique LLVM local name for <paramref name="name"/>, handling shadowing.</summary>
    private string NextUniqueLocalName(string name)
    {
        if (_varNameCounts.TryGetValue(key: name, value: out int count))
        {
            _varNameCounts[key: name] = count + 1;
            return $"{name}.{count + 1}";
        }

        _varNameCounts[key: name] = 1;
        return name;
    }

    /// <summary>
    /// Zero-initializes the entry slot of an owned local: a bare entity built by a constructor (or a
    /// lateinit placeholder) and an RC-wrapper local. A declaration inside a not-taken branch still has
    /// its slot, and the use-after-steal null guard and teardown both read it, so it must start null.
    /// </summary>
    private void ZeroInitOwnedLocalSlot(VariableDeclaration varDecl, TypeSymbol varType, string varPtr)
    {
        if (varType is EntityTypeSymbol && (IsEntityConstructorCall(expr: varDecl.Initializer) ||
                                            varDecl.IsLateInit && varDecl.Initializer == null))
        {
            EmitLine(sb: _currentRoutineEntryAllocas, line: $"  store ptr null, ptr {varPtr}");
            return;
        }

        if (varType is RecordTypeSymbol rcWrapRecord &&
            GetGenericBaseName(type: rcWrapRecord) is { } rcWrapBase &&
            RcWrapperBaseNames.Contains(item: rcWrapBase))
        {
            EmitLine(sb: _currentRoutineEntryAllocas,
                line: $"  store {GetLlvmType(type: rcWrapRecord)} zeroinitializer, ptr {varPtr}");
        }
    }

    /// <summary>
    /// Emits the eager allocation for a <c>lateinit var</c> with no initializer: a real heap block
    /// for entities (so the binding is immediately valid/borrowable and teardown frees a real
    /// allocation), or a zeroed value slot otherwise. No-op for a non-lateinit uninitialized decl.
    /// </summary>
    private void EmitLateInitPlaceholder(StringBuilder sb, VariableDeclaration varDecl,
        TypeSymbol varType, string llvmType, string varPtr)
    {
        if (!varDecl.IsLateInit)
        {
            return;
        }

        // The block must be calloc-backed (rf_allocate_dynamic, NOT _uninit): destroy runs on the
        // placeholder and walks its fields — zeroed fields are null-safe to free, garbage fields are
        // wild pointers. Zeroed contents are teardown armor, not a language guarantee.
        if (varType is EntityTypeSymbol lateInitEntity)
        {
            int blockSize = lateInitEntity.HeapBlockSize(pointerSize: _pointerSizeBytes);
            string placeholder = NextTemp();
            EmitLine(sb: sb,
                line: $"  {placeholder} = call ptr @rf_allocate_dynamic(i64 {blockSize})");
            EmitLine(sb: sb, line: $"  store ptr {placeholder}, ptr {varPtr}");
            return;
        }

        EmitLine(sb: sb, line: $"  store {llvmType} {GetZeroValue(type: varType)}, ptr {varPtr}");
    }

    /// <summary>
    /// When the declaration has an explicit type annotation, emits an inline primitive cast so the
    /// stored value's LLVM type matches the alloca type (e.g. <c>var e: U32 = s128Expr</c> truncs).
    /// Only applies between scalar @llvm-annotated records; aggregates share shape and need no cast.
    /// </summary>
    private string CoerceInitializerToDeclaredType(StringBuilder sb, VariableDeclaration varDecl,
        TypeSymbol varType, string llvmType, string value)
    {
        if (varDecl.Type == null)
        {
            return value;
        }

        TypeSymbol? initType = GetExpressionType(expr: varDecl.Initializer!);
        if (initType == null)
        {
            return value;
        }

        string initLlvm = GetLlvmType(type: initType);
        bool initIsScalar = initType is RecordTypeSymbol { BackendType: not null };
        bool varIsScalar = varType is RecordTypeSymbol { BackendType: not null };
        return initLlvm != llvmType && initIsScalar && varIsScalar
            ? EmitPrimitiveCast(sb: sb,
                value: value,
                fromLlvm: initLlvm,
                toLlvm: llvmType)
            : value;
    }

    /// <summary>
    /// Resolves the variable decl type from semantic compiler state.
    /// </summary>
    private TypeSymbol? ResolveVariableDeclType(VariableDeclaration varDecl)
    {
        TypeSymbol? varType = null;
        if (varDecl.Type != null)
        {
            varType = ResolveTypeExpression(typeExpr: varDecl.Type);
        }

        // Declared-type resolution failed (a bare cross-module annotation whose TypeExpression lost its
        // SA-stamped ResolvedType during a body-reconstructing pass — e.g. failable-variant expansion of
        // `var abs_val: Integer = …` in `Integer.to_digit_bytes!()`, IO referencing the Numerics `Integer`,
        // which codegen cannot re-resolve by bare name without the short-name scan). Fall back to the
        // initializer's own resolved type (a hoisted temp identifier already carries it).
        if (varType is null or ErrorTypeSymbol && varDecl.Initializer != null)
        {
            varType = GetExpressionType(expr: varDecl.Initializer) ?? varType;
        }

        // Fall back to the call's explicit generic-return-type resolution only when the
        // inferred varType is null or unresolved-generic. The earlier "ptr-typed" heuristic
        // was too loose — for `var x = entity.retain()`, the initializer's ResolvedType is
        // the fully-substituted `Retained[Entity[S64]]`, but the underlying routine's
        // declared ReturnType is the universal-memberRoutine-baked `Retained[Entity]` (with the
        // inner type-arg lost). TryResolveExplicitGenericCallReturnType reads
        // `routine.ReturnType` directly and would overwrite our correct varType with the
        // bare form. Only re-resolve when the existing varType is missing or still has
        // unresolved generic parameters.
        bool varTypeIsUnresolved = varType is null || varType is ErrorTypeSymbol ||
                                   varType is GenericParameterTypeSymbol ||
                                   ContainsGenericParameter(type: varType);
        if (varDecl.Initializer is CallExpression genericCallInit && varTypeIsUnresolved)
        {
            TypeSymbol? explicitGenericReturn =
                TryResolveExplicitGenericCallReturnType(call: genericCallInit);
            if (explicitGenericReturn != null)
            {
                varType = explicitGenericReturn;
            }
        }

        if (varType == null && varDecl.Initializer is CallExpression
            {
                ConstructedType: { } constructedType
            })
        {
            varType = constructedType;
        }

        // No name-based fuzzy fallback: the type must come structurally (declared type, initializer's
        // ResolvedType, the call's generic-return, or ConstructedType). If none resolved, the caller
        // hard-errors (UndeterminableVariableType) — codegen never fails silently, never string-parses a name.
        return varType;
    }

    /// <summary>
    /// Resolves a type expression to a TypeSymbol.
    /// </summary>
    private TypeSymbol? ResolveTypeExpression(TypeExpression typeExpr)
    {
        return ResolveTypeArgument(ta: typeExpr);
    }

    /// <summary>
    /// Attempts to resolve explicit generic call return type and reports whether it succeeded.
    /// </summary>
    private TypeSymbol? TryResolveExplicitGenericCallReturnType(CallExpression call)
    {
        if (call.ConstructedType is not null and not ErrorTypeSymbol)
        {
            return call.ConstructedType;
        }

        RoutineInfo? routine = call.ResolvedRoutine;

        if (routine == null || call.TypeArguments is not { Count: > 0 } explicitTypeArgs)
        {
            return routine?.ReturnType;
        }

        if (routine is
                { IsGenericDefinition: true, GenericParameters: { Count: > 0 } genericParams } &&
            explicitTypeArgs.Count == genericParams.Count)
        {
            var resolvedTypeArgs = explicitTypeArgs
                                  .Select(selector: selector =>
                                       ResolveTypeExpression(typeExpr: selector))
                                  .Where(predicate: t => t != null)
                                  .Cast<TypeSymbol>()
                                  .ToList();
            if (resolvedTypeArgs.Count == explicitTypeArgs.Count)
            {
                routine = _registry.GetOrCreateRoutineResolution(genericDef: routine,
                    typeArguments: resolvedTypeArgs);
            }
        }

        return routine.ReturnType;
    }

    #endregion

    #region Assignments

    /// <summary>
    /// Emits code for an assignment statement.
    /// Handles simple variable assignment and member variable assignment.
    /// </summary>
    private void EmitAssignment(StringBuilder sb, AssignmentStatement assign)
    {
        // Evaluate the value first
        string value = EmitExpression(sb: sb, expr: assign.Value);

        // Determine target type and emit store
        switch (assign.Target)
        {
            case IdentifierExpression id:
                EmitVariableAssignment(sb: sb, varName: id.Name, value: value);
                break;

            case MemberExpression member:
                EmitMemberVariableAssignment(sb: sb,
                    member: member,
                    value: value,
                    valueType: GetExpressionType(expr: assign.Value));
                NullStampStolenLocal(sb: sb, expr: assign.Value);
                break;

            case IndexExpression index:
                EmitIndexAssignment(sb: sb, index: index, rhs: assign.Value);
                break;

            default:
                throw new NotImplementedException(
                    message: $"Assignment target not implemented: {assign.Target.GetType().Name}");
        }
    }

    /// <summary>
    /// After a consuming use marked by StealGuardLoweringPass, stores null into the moved-out local's
    /// slot so a later use of the binding hits the use-after-steal null guard instead of a stale pointer.
    /// </summary>
    private void NullStampStolenLocal(StringBuilder sb, Expression expr)
    {
        Expression unwrapped = expr is NamedArgumentExpression named
            ? named.Value
            : expr;
        if (unwrapped is StealExpression steal)
        {
            unwrapped = steal.Operand;
        }

        if (unwrapped is not IdentifierExpression { NullStampAfterMove: true } id)
        {
            return;
        }

        string llvmName = _localVarLlvmNames.TryGetValue(key: id.Name, value: out string? unique)
            ? unique
            : id.Name;
        EmitLine(sb: sb, line: $"  store ptr null, ptr %{llvmName}.addr");
    }

    /// <summary>
    /// Emits a lowered <see cref="AtomicRmwStatement"/> as one sequentially consistent <c>atomicrmw</c> on
    /// the field's address. The instruction follows the field type: <c>fadd</c>/<c>fsub</c> for a float,
    /// <c>add</c>/<c>sub</c> for an integer.
    /// </summary>
    private void EmitAtomicRmw(StringBuilder sb, AtomicRmwStatement atomic)
    {
        string fieldPtr = EmitRoamedEntityFieldAddress(sb: sb, fieldMember: atomic.Field);
        TypeSymbol fieldType = atomic.Field.ResolvedType ??
                               throw new InvalidOperationException(
                                   message: "An atomic read-modify-write field has no resolved type.");
        string llvmType = GetValueLlvmType(type: fieldType);
        bool isFloat = llvmType is "float" or "double";
        string op = (atomic.Operation, isFloat) switch
        {
            (AtomicRmwOperation.Add, true) => "fadd",
            (AtomicRmwOperation.Subtract, true) => "fsub",
            (AtomicRmwOperation.Add, false) => "add",
            _ => "sub"
        };
        string deltaVal = EmitExpression(sb: sb, expr: atomic.Delta);
        string old = NextTemp();
        EmitLine(sb: sb, line: $"  {old} = atomicrmw {op} ptr {fieldPtr}, {llvmType} {deltaVal} seq_cst");
    }

    /// <summary>Projects a field access through a <c>Roamed[E]</c> handle to the field's ADDRESS: read
    /// the entity ptr from the roam controller's <c>data</c>, then GEP to the field.</summary>
    private string EmitRoamedEntityFieldAddress(StringBuilder sb, MemberExpression fieldMember)
    {
        EntityTypeSymbol entity = fieldMember.Object.ResolvedType switch
        {
            WrapperTypeSymbol { Name: Declaration.RuntimeContract.Roamed, InnerType: EntityTypeSymbol e } => e,
            RecordTypeSymbol
            {
                GenericDefinition.Name: Declaration.RuntimeContract.Roamed,
                TypeArguments: [EntityTypeSymbol e]
            } => e,
            _ => throw new InvalidOperationException(
                message: $"Field '{fieldMember.MemberName}' is not reached through a Roamed[E] handle.")
        };
        string handle = EmitExpression(sb: sb, expr: fieldMember.Object);
        EntityTypeSymbol controller = _registry.GetControllerType(wrapper: fieldMember.Object.ResolvedType!) ??
                                      throw new InvalidOperationException(
                                          message: $"Roamed handle for '{entity.Name}' has no controller type.");
        string entityPtr = ReadControllerData(sb: sb, handle: handle, controller: controller);
        return EmitEntityMemberVariableFieldPointer(sb: sb,
            entityPtr: entityPtr,
            entity: entity,
            memberVariableName: fieldMember.MemberName);
    }

    /// <summary>The GEP pointer to an entity member variable (the address, without the load that
    /// <see cref="EmitEntityMemberVariableRead"/> appends).</summary>
    private string EmitEntityMemberVariableFieldPointer(StringBuilder sb, string entityPtr,
        EntityTypeSymbol entity, string memberVariableName)
    {
        entity = RefreshEntityMemberVariables(entity: entity,
            memberVariableName: memberVariableName);
        GenerateEntityType(entity: entity);

        int idx = -1;
        for (int i = 0; i < entity.MemberVariables.Count; i++)
        {
            if (entity.MemberVariables[index: i].Name == memberVariableName)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
        {
            throw new InvalidOperationException(
                message:
                $"Member variable '{memberVariableName}' not found on entity '{entity.FullName}'");
        }

        string typeName = GetEntityTypeName(entity: entity);
        string ptr = NextTemp();
        EmitLine(sb: sb,
            line: $"  {ptr} = getelementptr {typeName}, ptr {entityPtr}, i32 0, i32 {idx}");
        return ptr;
    }

    /// <summary>
    /// Emits a store to a local variable. Releasing an owning old value is already in the AST
    /// (TemporaryTeardownPass / ScopeTeardownLoweringPass), so this is a plain store.
    /// </summary>
    private void EmitVariableAssignment(StringBuilder sb, string varName, string value)
    {
        if (!_localVariables.TryGetValue(key: varName, value: out TypeSymbol? varType))
        {
            // Suflae module-level `global`: store to its `@global` symbol.
            if (_moduleGlobals.TryGetValue(key: varName,
                    value: out (TypeSymbol Type, string Symbol) gslot))
            {
                EmitLine(sb: sb,
                    line:
                    $"  store {GetValueLlvmType(type: gslot.Type)} {value}, ptr {gslot.Symbol}");
                return;
            }

            throw new InvalidOperationException(message: $"Variable '{varName}' not found");
        }

        string llvmName = _localVarLlvmNames.TryGetValue(key: varName, value: out string? unique)
            ? unique
            : varName;
        string llvmType = GetValueLlvmType(type: varType);
        string varPtr = $"%{llvmName}.addr";
        EmitLine(sb: sb, line: $"  store {llvmType} {value}, ptr {varPtr}");
    }

    /// <summary>
    /// Emits a store to a member variable.
    /// </summary>
    private void EmitMemberVariableAssignment(StringBuilder sb, MemberExpression member,
        string value, TypeSymbol? valueType = null)
    {
        TypeSymbol? targetType = GetExpressionType(expr: member.Object);
        targetType = MarkerProtocolInner(type: targetType) ?? targetType;

        // Struct-record field write (no @llvm backend type): address-based. EmitLvalueAddress
        // computes the record's storage address and recurses through arbitrary lvalue chains
        // (`x.field`, `a.b.c`, `me.inner`, …), so this is the single path for every struct-record
        // field assignment — not just bare-local identifiers. GEP to the field index and store.
        // Wrapper records (`@llvm("ptr")`) and entities have backend types / pointer identity and
        // are handled by the value-based branches below.
        if (targetType is RecordTypeSymbol { BackendType: null } structRecord &&
            !(GetGenericBaseName(type: structRecord) is { } srBase &&
              WrapperTypeNames.Contains(item: srBase)))
        {
            EmitStructRecordMemberVariableWrite(sb: sb,
                member: member,
                value: value,
                structRecord: structRecord);
            return;
        }

        // Evaluate the object as a value (entity ptr / wrapper ptr) for the remaining branches.
        string target = EmitExpression(sb: sb, expr: member.Object);

        if (targetType is EntityTypeSymbol entity)
        {
            EmitEntityMemberVariableWrite(sb: sb,
                entityPtr: target,
                entity: entity,
                memberVariableName: member.MemberName,
                value: value,
                valueType: valueType);
        }
        // Wrapper-of-record field write: Modifying[Record] etc. The wrapper is `@llvm("ptr")`
        // and the pointer addresses a record value in memory. GEP into the record at the
        // field index and store. (Record-inner branch must come before the entity-inner one
        // since RecordTypeSymbol and EntityTypeSymbol are distinct AST nodes.)
        else if (targetType is RecordTypeSymbol wrapperRecOfRec &&
                 GetGenericBaseName(type: wrapperRecOfRec) is { } wrapRecBaseName &&
                 WrapperTypeNames.Contains(item: wrapRecBaseName) &&
                 wrapperRecOfRec is { BackendType: not null, TypeArguments.Count: > 0 } &&
                 wrapperRecOfRec.TypeArguments[index: 0] is RecordTypeSymbol innerRecord &&
                 !wrapperRecOfRec.MemberVariables.Any(
                     predicate: mv => mv.Name == member.MemberName))
        {
            EmitWrapperOfRecordMemberVariableWrite(sb: sb,
                member: member,
                value: value,
                target: target,
                innerRecord: innerRecord);
        }
        // Wrapper type forwarding: Modifying[T], Amending[T], etc. -> write through to inner entity
        else if (targetType is RecordTypeSymbol wrapperRecord &&
                 GetGenericBaseName(type: wrapperRecord) is { } wrapBaseName &&
                 WrapperTypeNames.Contains(item: wrapBaseName) &&
                 wrapperRecord.TypeArguments is { Count: > 0 } &&
                 wrapperRecord.TypeArguments[index: 0] is EntityTypeSymbol innerEntity)
        {
            EmitWrapperForwardingMemberVariableWrite(sb: sb,
                member: member,
                value: value,
                valueType: valueType,
                ctx: new WrapperWriteContext(Target: target,
                    WrapperRecord: wrapperRecord,
                    WrapBaseName: wrapBaseName,
                    InnerEntity: innerEntity));
        }
        else
        {
            throw new InvalidOperationException(
                message: $"Cannot assign to member variable on type: {targetType?.Name}");
        }
    }

    /// <summary>Address-based store into a struct-record field (no @llvm backend type).</summary>
    private void EmitStructRecordMemberVariableWrite(StringBuilder sb, MemberExpression member,
        string value, RecordTypeSymbol structRecord)
    {
        int sfIndex = -1;
        MemberVariableInfo? sfInfo = null;
        for (int i = 0; i < structRecord.MemberVariables.Count; i++)
        {
            if (structRecord.MemberVariables[index: i].Name == member.MemberName)
            {
                sfIndex = i;
                sfInfo = structRecord.MemberVariables[index: i];
                break;
            }
        }

        if (sfIndex < 0 || sfInfo == null)
        {
            throw new InvalidOperationException(
                message:
                $"Member variable '{member.MemberName}' not found on record '{structRecord.Name}'");
        }

        string structAddr = EmitLvalueAddress(sb: sb, expr: member.Object);
        string structTypeName = EnsureRecordTypeDeclared(record: structRecord);
        string sFieldPtr = NextTemp();
        EmitLine(sb: sb,
            line:
            $"  {sFieldPtr} = getelementptr {structTypeName}, ptr {structAddr}, i32 0, i32 {sfIndex}");
        EmitLine(sb: sb,
            line: $"  store {GetLlvmType(type: sfInfo.Type)} {value}, ptr {sFieldPtr}");
    }

    /// <summary>GEP-and-store into the record addressed by a <c>@llvm("ptr")</c> wrapper-of-record
    /// (Modifying[Record] etc.), where <paramref name="target"/> is the loaded wrapper pointer.</summary>
    private void EmitWrapperOfRecordMemberVariableWrite(StringBuilder sb, MemberExpression member,
        string value, string target, RecordTypeSymbol innerRecord)
    {
        int fieldIndex = -1;
        MemberVariableInfo? fieldInfo = null;
        for (int i = 0; i < innerRecord.MemberVariables.Count; i++)
        {
            if (innerRecord.MemberVariables[index: i].Name == member.MemberName)
            {
                fieldIndex = i;
                fieldInfo = innerRecord.MemberVariables[index: i];
                break;
            }
        }

        if (fieldIndex < 0 || fieldInfo == null)
        {
            throw new InvalidOperationException(
                message:
                $"Member '{member.MemberName}' not found on inner record '{innerRecord.Name}'");
        }

        string innerRecordTypeName = EnsureRecordTypeDeclared(record: innerRecord);
        string fieldPtr = NextTemp();
        EmitLine(sb: sb,
            line:
            $"  {fieldPtr} = getelementptr {innerRecordTypeName}, ptr {target}, i32 0, i32 {fieldIndex}");
        EmitLine(sb: sb,
            line: $"  store {GetLlvmType(type: fieldInfo.Type)} {value}, ptr {fieldPtr}");
    }

    /// <summary>Forwards a field write through a wrapper (Modifying[T], Retained[T], Roamed[T], …) to
    /// the inner entity, projecting through the controller's <c>data</c> where needed.</summary>
    private void EmitWrapperForwardingMemberVariableWrite(StringBuilder sb,
        MemberExpression member, string value, TypeSymbol? valueType,
        WrapperWriteContext ctx)
    {
        // Roamed[T] projects through RoamController.data and writes directly — handled separately
        // because the access-lock bracket is already inserted around the whole statement by
        // RoamedLockBracketLoweringPass; codegen just projects + stores here.
        if (ctx.WrapperRecord.BackendType != null &&
            ctx.WrapBaseName == Declaration.RuntimeContract.Roamed)
        {
            EmitRoamedWrapperMemberVariableWrite(sb: sb,
                member: member,
                value: value,
                valueType: valueType,
                ctx: ctx);
            return;
        }

        string innerPtr = ResolveWrapperInnerEntityPtr(sb: sb, ctx: ctx);
        EmitEntityMemberVariableWrite(sb: sb,
            entityPtr: innerPtr,
            entity: ctx.InnerEntity,
            memberVariableName: member.MemberName,
            value: value,
            valueType: valueType);
    }

    /// <summary>Emits a Roamed[T] wrapper field write by projecting through <c>RoamController.data</c>.</summary>
    private void EmitRoamedWrapperMemberVariableWrite(StringBuilder sb, MemberExpression member,
        string value, TypeSymbol? valueType, WrapperWriteContext ctx)
    {
        EntityTypeSymbol controller = _registry.GetControllerType(wrapper: ctx.WrapperRecord) ??
                                      throw new InvalidOperationException(
                                          message: $"'{ctx.WrapperRecord.Name}' has no controller type.");
        string roamEntPtr = ReadControllerData(sb: sb, handle: ctx.Target, controller: controller);
        EmitEntityMemberVariableWrite(sb: sb,
            entityPtr: roamEntPtr,
            entity: ctx.InnerEntity,
            memberVariableName: member.MemberName,
            value: value,
            valueType: valueType);
    }

    /// <summary>
    /// Resolves the inner entity pointer from a wrapper target — projecting through the controller's
    /// <c>data</c> field for Retained/Tracked, or extracting the Hijacked field for struct wrappers.
    /// </summary>
    private string ResolveWrapperInnerEntityPtr(StringBuilder sb, WrapperWriteContext ctx)
    {
        string target = ctx.Target;
        RecordTypeSymbol wrapperRecord = ctx.WrapperRecord;
        EntityTypeSymbol innerEntity = ctx.InnerEntity;

        // A controller-backed wrapper: the entity lives in the controller's `data` field. Without this,
        // writes would store into the controller's counts.
        if (wrapperRecord.BackendType != null &&
            _registry.GetControllerType(wrapper: wrapperRecord) is { } controller)
        {
            return ReadControllerData(sb: sb, handle: target, controller: controller);
        }

        // Other @llvm("ptr") wrappers: the pointer IS the inner entity directly.
        if (wrapperRecord.BackendType != null)
        {
            return target;
        }

        // Struct wrapper: extract the Hijacked[T] field that holds the inner entity pointer.
        string recordTypeName = EnsureRecordTypeDeclared(record: wrapperRecord);
        string innerPtr = NextTemp();
        int dataFieldIndex =
            FindHijackedFieldIndex(wrapperRecord: wrapperRecord, innerEntity: innerEntity);
        EmitLine(sb: sb,
            line: $"  {innerPtr} = extractvalue {recordTypeName} {target}, {dataFieldIndex}");
        return innerPtr;
    }


    /// <summary>
    /// Emits an index assignment that has no <c>setitem</c>: a raw GEP + store into contiguous
    /// storage. Every <c>a[i] = v</c> on a type with a <c>setitem</c> was already lowered to that call
    /// by OperatorLoweringPass, so reaching here with one is a lowering bug, reported loudly.
    /// </summary>
    private void EmitIndexAssignment(StringBuilder sb, IndexExpression index, Expression rhs)
    {
        TypeSymbol? targetType = GetExpressionType(expr: index.Object);
        if (targetType != null &&
            _registry.LookupMemberRoutine(type: targetType, memberRoutineName: "setitem") != null)
        {
            throw new InvalidOperationException(
                message: $"Index assignment on '{targetType.Name}' reached the emitter unlowered " +
                         $"(at {index.Location}); OperatorLoweringPass should have turned it into a " +
                         "setitem call.");
        }

        EmitRawIndexStore(sb: sb,
            index: index,
            rhs: rhs,
            targetType: targetType);
    }

    /// <summary>
    /// Fallback index store: raw GEP + store for pointer/contiguous-memory types with no
    /// <c>setitem</c> memberRoutine.
    /// </summary>
    private void EmitRawIndexStore(StringBuilder sb, IndexExpression index, Expression rhs,
        TypeSymbol? targetType)
    {
        string rawValue = EmitExpression(sb: sb, expr: rhs);
        string target = EmitExpression(sb: sb, expr: index.Object);
        string idxVal = EmitExpression(sb: sb, expr: index.Index);

        string elemType = targetType switch
        {
            RecordTypeSymbol { TypeArguments.Count: > 0 } r => GetLlvmType(
                type: r.TypeArguments![index: 0]),
            EntityTypeSymbol { TypeArguments.Count: > 0 } e => GetLlvmType(
                type: e.TypeArguments![index: 0]),
            _ => throw new InvalidOperationException(
                message:
                $"Cannot determine element type for index assignment on type: {targetType?.Name}")
        };

        string elemPtr = NextTemp();
        EmitLine(sb: sb,
            line: $"  {elemPtr} = getelementptr {elemType}, ptr {target}, i64 {idxVal}");
        EmitLine(sb: sb, line: $"  store {elemType} {rawValue}, ptr {elemPtr}");
    }

    #endregion

    /// <summary>RC wrapper base names, whose local slots start zeroed. Single source of truth is
    /// <see cref="Declaration.RuntimeContract.RcWrapperBaseNames"/>.</summary>
    private static readonly IReadOnlySet<string> RcWrapperBaseNames =
        Declaration.RuntimeContract.RcWrapperBaseNames;

    // -----------------------------------------------------------------------------

    /// <summary>
    /// Emit if as part of this compiler phase.
    /// </summary>
    private bool EmitIf(StringBuilder sb, IfStatement ifStmt)
    {
        string condition = EmitExpression(sb: sb, expr: ifStmt.Condition);

        string thenLabel = NextLabel(prefix: "if_then");
        string endLabel = NextLabel(prefix: "if_end");

        return ifStmt.ElseBranch != null
            ? EmitIfElse(sb: sb,
                ifStmt: ifStmt,
                condition: condition,
                thenLabel: thenLabel,
                endLabel: endLabel)
            : EmitIfNoElse(sb: sb,
                ifStmt: ifStmt,
                condition: condition,
                thenLabel: thenLabel,
                endLabel: endLabel);
    }

    /// <summary>Emits an <c>if</c> WITH an else branch; returns true when both branches terminate.</summary>
    private bool EmitIfElse(StringBuilder sb, IfStatement ifStmt, string condition,
        string thenLabel, string endLabel)
    {
        string elseLabel = NextLabel(prefix: "if_else");
        EmitLine(sb: sb, line: $"  br i1 {condition}, label %{thenLabel}, label %{elseLabel}");

        // Then branch
        EmitLine(sb: sb, line: $"{thenLabel}:");
        bool thenTerminated = EmitStatement(sb: sb, stmt: ifStmt.ThenBranch);
        if (!thenTerminated)
        {
            EmitLine(sb: sb, line: $"  br label %{endLabel}");
        }

        // Else branch
        EmitLine(sb: sb, line: $"{elseLabel}:");
        bool elseTerminated = EmitStatement(sb: sb, stmt: ifStmt.ElseBranch!);
        if (!elseTerminated)
        {
            EmitLine(sb: sb, line: $"  br label %{endLabel}");
        }

        // If both branches terminated, the end block is unreachable
        // but we still need to emit it for LLVM (it will be dead code eliminated)
        if (thenTerminated && elseTerminated)
        {
            // Both branches return - the if statement as a whole terminates
            // Emit end label + unreachable (dead block must still have a terminator)
            EmitLine(sb: sb, line: $"{endLabel}:");
            EmitLine(sb: sb, line: "  unreachable");
            return true;
        }

        // End block is reachable from at least one branch
        EmitLine(sb: sb, line: $"{endLabel}:");
        return false;
    }

    /// <summary>Emits an <c>if</c> WITHOUT an else branch; never fully terminates.</summary>
    private bool EmitIfNoElse(StringBuilder sb, IfStatement ifStmt, string condition,
        string thenLabel, string endLabel)
    {
        EmitLine(sb: sb, line: $"  br i1 {condition}, label %{thenLabel}, label %{endLabel}");

        // Then branch
        EmitLine(sb: sb, line: $"{thenLabel}:");
        bool thenTerminated = EmitStatement(sb: sb, stmt: ifStmt.ThenBranch);
        if (!thenTerminated)
        {
            EmitLine(sb: sb, line: $"  br label %{endLabel}");
        }

        // End block (always reachable via the else path, even if then returns)
        EmitLine(sb: sb, line: $"{endLabel}:");
        return false; // If without else never fully terminates
    }

    /// <summary>
    /// Stack of loop labels for break/continue.
    /// </summary>
    private readonly Stack<(string ContinueLabel, string BreakLabel)> _loopStack = new();

    /// <summary>
    /// Emits code for a loop statement (infinite loop primitive).
    /// Unconditional back-edge: continue -> loop header, break -> end.
    /// </summary>
    private void EmitLoop(StringBuilder sb, LoopStatement loopStmt)
    {
        string bodyLabel = NextLabel(prefix: "loop_body");
        string endLabel = NextLabel(prefix: "loop_end");

        // Push loop labels: continue -> body header, break -> end
        _loopStack.Push(item: (bodyLabel, endLabel));

        // Jump to body
        EmitLine(sb: sb, line: $"  br label %{bodyLabel}");

        // Body block
        EmitLine(sb: sb, line: $"{bodyLabel}:");
        bool bodyTerminated = EmitStatement(sb: sb, stmt: loopStmt.Body);
        if (!bodyTerminated)
        {
            EmitLine(sb: sb, line: $"  br label %{bodyLabel}");
        }

        // End block
        EmitLine(sb: sb, line: $"{endLabel}:");

        _loopStack.Pop();
    }


    /// <summary>
    /// Emits code for a break statement.
    /// </summary>
    private void EmitBreak(StringBuilder sb)
    {
        if (_loopStack.Count == 0)
        {
            throw new InvalidOperationException(message: "Break statement outside of loop");
        }

        (_, string breakLabel) = _loopStack.Peek();
        EmitLine(sb: sb, line: $"  br label %{breakLabel}");
    }

    /// <summary>
    /// Emits code for a continue statement.
    /// </summary>
    private void EmitContinue(StringBuilder sb)
    {
        if (_loopStack.Count == 0)
        {
            throw new InvalidOperationException(message: "Continue statement outside of loop");
        }

        (string continueLabel, _) = _loopStack.Peek();
        EmitLine(sb: sb, line: $"  br label %{continueLabel}");
    }
}

/// <summary>
/// Bundles the wrapper-related arguments for <see cref="LlvmEmitter.EmitWrapperForwardingMemberVariableWrite"/>
/// so the method stays within the parameter-count limit.
/// </summary>
internal sealed record WrapperWriteContext(
    string Target,
    RecordTypeSymbol WrapperRecord,
    string WrapBaseName,
    EntityTypeSymbol InnerEntity);
