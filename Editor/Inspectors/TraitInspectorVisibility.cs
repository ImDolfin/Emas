using UnityEditor;
using UnityEngine;

namespace Emas.Editor
{
    /// <summary>Keeps root-trait component cards hidden while their Ghost supplies the authoring controls.</summary>
    [InitializeOnLoad]
    internal static class TraitInspectorVisibility
    {
        private static bool _refreshQueued;

        static TraitInspectorVisibility()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            Undo.undoRedoPerformed += QueueRefresh;
            Selection.selectionChanged += QueueRefresh;
            QueueRefresh();
        }

        internal static void QueueRefresh()
        {
            // Inspector creation, drawing and selection callbacks may run while Unity is
            // rebuilding its component cards. Always change hide flags after that pass.
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
            EditorApplication.delayCall += RefreshLoadedTraits;
        }

        private static void OnHierarchyChanged()
        {
            // Do not scan the whole scene for ordinary tracked movement. Component
            // structure changes still queue a refresh through OnObjectChanges in Play Mode.
            if (!EditorApplication.isPlaying)
            {
                QueueRefresh();
            }
        }

        private static void OnObjectChanges(ref ObjectChangeEventStream changes)
        {
            // Scan only when components or prefabs change. Editing a value does not change who owns a trait.
            for (int index = 0; index < changes.length; index++)
            {
                ObjectChangeKind kind = changes.GetEventType(index);
                if (kind == ObjectChangeKind.ChangeGameObjectStructure ||
                    kind == ObjectChangeKind.ChangeGameObjectStructureHierarchy ||
                    kind == ObjectChangeKind.UpdatePrefabInstances)
                {
                    QueueRefresh();
                    return;
                }
            }
        }

        private static void RefreshLoadedTraits()
        {
            _refreshQueued = false;
            // Include inactive roots, prefab assets and Prefab Mode. Also fix old flags saved
            // before scripts reloaded or a Ghost changed while no Inspector was open.
            foreach (Trait trait in Resources.FindObjectsOfTypeAll<Trait>())
            {
                if (trait != null)
                {
                    SetVisibility(trait, trait.GetComponent<Ghost>() != null);
                }
            }
        }

        private static void SetVisibility(Trait trait, bool hasGhost)
        {
            if (trait == null)
            {
                return;
            }

            // Change only Inspector visibility, leaving NotEditable and DontSave alone. Show any
            // remaining traits again when their Ghost is removed so their settings stay accessible.
            HideFlags flags = hasGhost ? trait.hideFlags | HideFlags.HideInInspector
                : trait.hideFlags & ~HideFlags.HideInInspector;
            if (trait.hideFlags != flags)
            {
                trait.hideFlags = flags;
            }
        }
    }
}
