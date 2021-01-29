#if !ROSLYN_SOURCEGEN_ENABLED
using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.Entities.CodeGen;
using Unity.Entities.CodeGen.Tests;
using UnityEngine;
#if ROSLYN_SOURCEGEN_ENABLED
using Unity.Entities.CodeGen.Tests.SourceGenerationTests;
#endif

namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    [TestFixture]
    class GenerateAuthoringComponentCompileTimeTests : PostProcessorTestBase
    {
#if !ROSLYN_SOURCEGEN_ENABLED
        [Test]
        public void GenerateAuthoringComponentAttributeWithNoValidInterfaceThrowsError()
        {
            AssertProducesError(
                typeof(GenerateAuthoringComponentWithNoValidInterface),
                shouldContainErrors: nameof(UserError.DC3003));
        }

        protected override void AssertProducesInternal(
            Type systemType,
            DiagnosticType expectedDiagnosticType,
            string[] errorIdentifiers,
            bool useFailResolver = false)
        {
            DiagnosticMessage error = null;

            try
            {
                AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(systemType.Assembly.Location);
                TypeDefinition typeDefinitionToTest = assemblyDefinition.MainModule.Types.Single(t => t.Name == systemType.Name);

                bool _ = AuthoringComponentPostProcessor.RunTest(typeDefinitionToTest);
            }
            catch (FoundErrorInUserCodeException exception)
            {
                error = exception.DiagnosticMessages.Single();
            }
            Assert.AreEqual(expected: expectedDiagnosticType, actual: error?.DiagnosticType);
            Assert.IsTrue(error?.MessageData.Contains(errorIdentifiers.Single()));
        }
#else
        [Test]
        public void GenerateAuthoringComponentAttributeWithNoValidInterfaceThrowsError()
        {
            var code =
                @"
                using Unity.Entities;

                [GenerateAuthoringComponent]
                public struct GenerateAuthoringComponentWithNoValidInterface
                {
                    public float Value;
                }";

            var compileResult =
                TestCompiler.Compile(code, new []
                {
                    typeof(GenerateAuthoringComponentAttribute),
                    typeof(ConvertToEntity),
                    typeof(GameObject),
                    typeof(MonoBehaviour)
                });

            Assert.IsFalse(compileResult.IsSuccess);
            Assert.IsTrue(compileResult.CompilerMessages.Any(msg =>
                msg.message.Contains("The [GenerateAuthoringComponent] attribute may only be used with types that implement either IBufferElementData or IComponentData")));
        }

        protected override void AssertProducesInternal(Type systemType, DiagnosticType type, string[] shouldContains, bool useFailResolver = false) { }
#endif
    }
}
#endif
