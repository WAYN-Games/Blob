#if ROSLYN_SOURCEGEN_ENABLED
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityTypeInvokedByEntitiesOnUpdate : JobEntitySourceGenerationTests
    {
        private readonly string Code =
             @"using Unity.Entities;
               using Unity.Mathematics;
               using Unity.Transforms;
               using UnityEngine;

               public struct RotateEntityJob : IJobEntity
               {
                    public float DeltaTime;

                    public void OnUpdate(ref Rotation rotation, in RotationSpeed_ForEach speed)
                    {
                        rotation.Value =
                            math.mul(
                                math.normalize(rotation.Value),
                                quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * DeltaTime));
                    }
               }

               public struct Rotation : IComponentData
               {
                    public quaternion Value;
               }

               public struct RotationSpeed_ForEach : IComponentData
               {
                    public float RadiansPerSecond;
               }

               public partial class RotationSpeedSystem_IJobEntity : SystemBase
               {
                    protected override void OnUpdate()
                    {
                        var rotateEntityJob = new RotateEntityJob { DeltaTime = Time.DeltaTime };
                        Entities.OnUpdate(rotateEntityJob).ScheduleParallel();
                    }
               }";

        [Test]
        public void JobEntityTypeInvokedInEntitiesOnUpdateTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "RotateEntityJob_OnUpdate",
                },
                new GeneratedType
                {
                    Name = "RotationSpeedSystem_IJobEntity"
                });
        }
    }
}
#endif
