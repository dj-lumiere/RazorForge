using Builder.Declaration;
using SyntaxTree;
using TypeModel.Symbols;
using TypeModel.Types;

namespace Builder.Lowering.Passes;

/// <summary>
/// Resolves, once the error types are concrete, the <c>crash_message()</c> each <c>throw</c> calls for
/// its crash text, and stamps it on the <see cref="ThrowStatement"/>. The emitter then only calls the
/// stamped routine instead of looking the member up itself. Runs at Phase 9 over every body that reaches
/// the emitter (after monomorphization, so a thrown value's type is concrete).
/// </summary>
internal static class CrashMessageStampPass
{
    public static void Run(Statement body, TypeRegistry registry)
    {
        AstWalker.Walk(root: body,
            visit: node =>
            {
                if (node is not ThrowStatement { Error.ResolvedType: { } errorType } throwStmt ||
                    errorType is ErrorTypeSymbol or GenericParameterTypeSymbol)
                {
                    return;
                }

                RoutineInfo? crashMessage = registry.LookupMemberRoutineOverload(type: errorType,
                    memberRoutineName: RuntimeContract.CrashMessage,
                    argTypes: new List<TypeSymbol>());
                throwStmt.CrashMessageRoutine = crashMessage is { IsGenericDefinition: false }
                    ? crashMessage
                    : null;
            });
    }
}
