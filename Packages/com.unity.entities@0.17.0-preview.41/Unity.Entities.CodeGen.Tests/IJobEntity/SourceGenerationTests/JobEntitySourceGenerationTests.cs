#if ROSLYN_SOURCEGEN_ENABLED
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntitySourceGenerationTests  : IntegrationTest
    {
        protected override string ExpectedPath =>
            "Packages/com.unity.entities/Unity.Entities.CodeGen.Tests/IJobEntity/SourceGenerationTests";

        protected void RunTest(string cSharpCode, params GeneratedType[] generatedTypes)
        {
            var (isSuccess, compilerMessages) = TestCompiler.Compile(cSharpCode, new []
            {
                typeof(Unity.Entities.SystemBase),
                typeof(Unity.Burst.BurstCompileAttribute),
                typeof(Unity.Mathematics.float3),
                typeof(Unity.Collections.ReadOnlyAttribute),
                typeof(Unity.Collections.LowLevel.Unsafe.UnsafeUtility)
            });

            if (!isSuccess)
            {
                Assert.Fail($"Compilation failed with errors {string.Join(Environment.NewLine, compilerMessages.Select(msg => msg.message))}");
            }

            RunSourceGenerationTest(generatedTypes, Path.Combine(TestCompiler.DirectoryForTestDll, TestCompiler.OutputDllName));
            TestCompiler.CleanUp();
        }
    }
}
#endif
