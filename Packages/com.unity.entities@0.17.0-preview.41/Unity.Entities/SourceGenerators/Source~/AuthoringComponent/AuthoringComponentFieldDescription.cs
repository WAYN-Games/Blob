using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    internal class AuthoringComponentFieldDescription
    {
        public IFieldSymbol FieldSymbol;
        public FieldType FieldType;

        public static AuthoringComponentFieldDescription From(VariableDeclaratorSyntax syntaxNode, GeneratorExecutionContext context)
        {
            IFieldSymbol fieldSymbol = (IFieldSymbol)context.Compilation.GetSemanticModel(syntaxNode.SyntaxTree).GetDeclaredSymbol(syntaxNode);

            return new AuthoringComponentFieldDescription
            {
                FieldSymbol = fieldSymbol,
                FieldType = GetFieldType(fieldSymbol)
            };
        }

        private static FieldType GetFieldType(IFieldSymbol fieldSymbol)
        {
            if (fieldSymbol.Type.Is(nameSpace: "Entities", typeName: "Entity"))
            {
                return FieldType.SingleEntity;
            }
            if (fieldSymbol.Type is IArrayTypeSymbol arrayTypeSymbol && arrayTypeSymbol.ElementType.Is("Entities", "Entity"))
            {
                return FieldType.EntityArray;
            }

            return
                fieldSymbol.Type.IsReferenceType
                    ? FieldType.NonEntityReferenceType
                    : FieldType.NonEntityValueType;
        }
    }
}

