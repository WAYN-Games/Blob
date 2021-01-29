#if UNITY_2020_2_OR_NEWER
using System.Collections.Generic;

namespace Unity.Entities
{
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    class SceneSectionIncrementalConversionSystem : SystemBase
    {
        readonly Dictionary<int, int> m_Section = new Dictionary<int, int>();
        IncrementalChangesSystem m_Incremental;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Incremental = World.GetExistingSystem<IncrementalChangesSystem>();
        }

        protected override void OnUpdate()
        {
            // delete information about all deleted instances
            var deletions = m_Incremental.IncomingChanges.RemovedGameObjectInstanceIds;
            for (int i = 0; i < deletions.Length; i++)
                m_Section.Remove(deletions[i]);

            // Update information for all changed instances. Those are all objects for which StructuralGameObjectChange
            // has been triggered (e.g. unknown component changes).
            var changes = m_Incremental.IncomingChanges.ChangedGameObjects;
            for (int i = 0; i < changes.Count; i++)
            {
                var go = changes[i];
                int instanceId = go.GetInstanceID();
                if (!go.TryGetComponent<SceneSectionComponent>(out var section))
                {
                    // if there was no section info previously, but now there is, reconvert the full subhierarchy
                    if (m_Section.Remove(instanceId))
                        m_Incremental.AddHierarchyConversionRequest(instanceId);
                }
                else if (!m_Section.TryGetValue(instanceId, out int previousSection) || section.SectionIndex != previousSection)
                {
                    // otherwise, if there was no section info before or the new section mismatches the old section,
                    // reconvert the full subhierarchy
                    m_Incremental.AddHierarchyConversionRequest(instanceId);
                    m_Section[instanceId] = section.SectionIndex;
                }
            }

            var changedComponents = m_Incremental.IncomingChanges.ChangedComponents;
            for (int i = 0; i < changedComponents.Count; i++)
            {
                var c = changedComponents[i];
                if (c is SceneSectionComponent section)
                {
                    int instanceId = section.gameObject.GetInstanceID();
                    if (!m_Section.TryGetValue(instanceId, out int sectionIndex) ||
                        sectionIndex != section.SectionIndex)
                    {
                        m_Section[instanceId] = sectionIndex;
                        m_Incremental.AddHierarchyConversionRequest(instanceId);
                    }
                }
            }
        }
    }
}
#endif
