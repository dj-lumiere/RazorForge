using Builder.Declaration;
using SyntaxTree;
using TypeModel.Types;

namespace Builder.Lowering.Passes;

/// <summary>
/// Marks the runtime use-after-steal net on a routine body, so the emitter only translates flags.
///
/// <para>Semantic analysis records every local a routine moves out with <c>steal</c> (or hands to a
/// thread) in <see cref="RoutineDeclaration.EverStolenVariableNames"/>. Static analysis proves most
/// uses after a move dead, but a loop, an alias or an indirect path can still reach the moved-out
/// binding. For each such local holding a movable handle (an entity or an RC wrapper), this pass sets:</para>
/// <list type="bullet">
/// <item><see cref="IdentifierExpression.StealGuarded"/> on every read, so the load is null-checked and
/// a null slot crashes loudly with <c>UseAfterStealError</c> instead of using a stale pointer.</item>
/// <item><see cref="IdentifierExpression.NullStampAfterMove"/> on a read in a consuming position (a call
/// or creator argument, a variable initializer, or the value of a member-variable write), so the slot is
/// set to null once the value has been handed over.</item>
/// </list>
/// <para>A <c>Roamed[T]</c> member-variable write copies (the field shares the handle), so its value is not
/// consumed. Bodies without a declaration (synthesized variant bodies) steal nothing and get no marks.
/// Runs at Phase 9, after every lowering, over each body that reaches the emitter.</para>
/// </summary>
internal static class StealGuardLoweringPass
{
    /// <summary>Marks the use-after-steal guards and null-stamps in one routine body.</summary>
    public static void Run(Statement body, HashSet<string>? everStolen)
    {
        if (everStolen is not { Count: > 0 })
        {
            return;
        }

        AstWalker.Walk(root: body,
            visit: node =>
            {
                switch (node)
                {
                    case IdentifierExpression id when IsGuarded(id: id, everStolen: everStolen):
                        id.StealGuarded = true;
                        break;
                    case CallExpression call:
                        MarkConsumed(operands: call.Arguments, everStolen: everStolen);
                        break;
                    case CreatorExpression creator:
                        foreach ((string _, Expression value) in creator.MemberVariables)
                        {
                            if (!IsRoamed(type: value.ResolvedType))
                            {
                                MarkConsumed(operand: value, everStolen: everStolen);
                            }
                        }

                        break;
                    case BinaryExpression { Operator: BinaryOperator.Assign, Left: MemberExpression } assign:
                        MarkConsumed(operand: assign.Right, everStolen: everStolen);
                        break;
                    case VariableDeclaration { Initializer: { } init }:
                        MarkConsumed(operand: init, everStolen: everStolen);
                        break;
                    case AssignmentStatement { Target: MemberExpression target } assign
                        when !IsRoamed(type: target.ResolvedType):
                        MarkConsumed(operand: assign.Value, everStolen: everStolen);
                        break;
                }
            });
    }

    private static void MarkConsumed(IEnumerable<Expression> operands, HashSet<string> everStolen)
    {
        foreach (Expression operand in operands)
        {
            MarkConsumed(operand: operand, everStolen: everStolen);
        }
    }

    private static void MarkConsumed(Expression operand, HashSet<string> everStolen)
    {
        Expression unwrapped = operand is NamedArgumentExpression named
            ? named.Value
            : operand;
        if (unwrapped is StealExpression steal)
        {
            unwrapped = steal.Operand;
        }

        if (unwrapped is IdentifierExpression id && IsGuarded(id: id, everStolen: everStolen))
        {
            id.NullStampAfterMove = true;
        }
    }

    /// <summary>True when the identifier names a moved-out local holding a movable handle: an entity
    /// or an RC wrapper, the values whose slot holds a pointer that <c>steal</c> hands over.</summary>
    private static bool IsGuarded(IdentifierExpression id, HashSet<string> everStolen)
    {
        return everStolen.Contains(item: id.Name) && id.ResolvedType is { } t &&
               (t is EntityTypeSymbol or WrapperTypeSymbol ||
                TypeRegistry.GetRcWrapperBaseName(type: t) is not null);
    }

    private static bool IsRoamed(TypeSymbol? type)
    {
        return type is not null &&
               TypeRegistry.GetRcWrapperBaseName(type: type) == RuntimeContract.Roamed;
    }
}
