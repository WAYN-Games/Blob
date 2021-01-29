using System;
using System.Linq;
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
    class BufferElementDataCompileTimeTests : PostProcessorTestBase
    {
#if !ROSLYN_SOURCEGEN_ENABLED
        [Test] // Source Generation, unlike IL post-processing, can handle IBufferElementData structs that contain multiple fields
        public void WrapAroundMultipleValuesThrowsError()
        {
            AssertProducesError(
                typeof(BufferElementDataWithMultipleWrappedValues),
                shouldContainErrors: nameof(UserError.DC0039));
        }
        [Test]
        public void BufferElementWithExplicitLayoutThrowsError()
        {
            AssertProducesError(
                typeof(BufferElementWithExplicitLayout),
                shouldContainErrors: nameof(UserError.DC0042));
        }

        protected override void AssertProducesInternal(Type systemType, DiagnosticType expectedDiagnosticType, string[] errorIdentifiers, bool useFailResolver = false)
        {
            DiagnosticMessage error = null;

            try
            {
                AuthoringComponentPostProcessor.CreateBufferElementDataAuthoringType(TypeDefinitionFor(systemType));
            }
            catch (FoundErrorInUserCodeException exception)
            {
                error = exception.DiagnosticMessages.Single();
            }

            Assert.AreEqual(expected: expectedDiagnosticType, actual: error?.DiagnosticType);
            Assert.IsTrue(error?.MessageData.Contains(errorIdentifiers.Single()));
        }
#else
        [Test] // Source Generation, unlike IL post-processing, can handle IBufferElementData with strict layout
        public void BufferElementWithExplicitLayoutThrowsNoError()
        {
            var code =
                @"
                using System.Runtime.InteropServices;
                using Unity.Entities;

                [StructLayout(LayoutKind.Explicit, Size = 10)]
                [GenerateAuthoringComponent]
                public struct BufferElementWithExplicitLayout : IBufferElementData
                {
                    [FieldOffset(3)] public byte Value;
                }";

            var compileResult =
                TestCompiler.Compile(code, new []
                    {
                        typeof(GenerateAuthoringComponentAttribute),
                        typeof(ConvertToEntity),
                        typeof(GameObject),
                        typeof(MonoBehaviour)
                    });

            Assert.IsTrue(compileResult.IsSuccess);
        }

        [Test]
        public void BufferElementWithEntityAndValueTypeThrowsNoError()
        {
            var code =
                @"
                using Unity.Entities;

                public struct SomeValueType { public int Value; }

                [GenerateAuthoringComponent]
                public struct BufferElementWithEntityArray : IBufferElementData
                {
                    public Entity Entity;
                    public SomeValueType ValueType;
                }";

            var compileResult =
                TestCompiler.Compile(code, new []
                {
                    typeof(GenerateAuthoringComponentAttribute),
                    typeof(ConvertToEntity),
                    typeof(GameObject),
                    typeof(MonoBehaviour)
                });

            Assert.IsTrue(compileResult.IsSuccess);
        }

        [Test]
        public void BufferElementWithEntityArrayThrowsError()
        {
            var code =
                @"
                using Unity.Entities;

                [GenerateAuthoringComponent]
                public struct BufferElementWithEntityArray : IBufferElementData
                {
                    public Entity[] EntityArray;
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
                msg.message.Contains("IBufferElementData types may only contain blittable or primitive fields.")));
        }

        [Test]
        public void BufferElementWithReferenceTypeThrowsError()
        {
            var code =
                @"
                using Unity.Entities;

                public class SomeRefType { }

                [GenerateAuthoringComponent]
                public struct BufferElementWithReferenceType : IBufferElementData
                {
                    public SomeRefType SomeRefType;
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
                msg.message.Contains("IBufferElementData types may only contain blittable or primitive fields.")));
        }

        protected override void AssertProducesInternal(Type systemType, DiagnosticType type, string[] shouldContains, bool useFailResolver = false) { }
#endif
    }
}
