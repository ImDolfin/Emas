using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Editor.Tests
{
    /// <summary>Exercises trait reordering through the Ghost Inspector's public GUI.</summary>
    public sealed class TraitReorderingTests
    {
        private sealed class InspectorWindow : EditorWindow
        {
            internal UnityEditor.Editor Inspector;
            internal Rect TraitsRect;
            internal Trait DragSource;

            private void OnGUI()
            {
                if (Inspector == null)
                {
                    return;
                }

                if (DragSource != null && (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform))
                {
                    // Inject public drag payloads at dispatch, without starting a desktop
                    // drag session whose real mouse position can interrupt a batch test.
                    DragAndDrop.objectReferences = new UnityEngine.Object[] { DragSource };
                }

                Inspector.OnInspectorGUI();
                if (Event.current.type == EventType.Repaint)
                {
                    TraitsRect = GUILayoutUtility.GetLastRect();
                }
            }
        }

        /// <summary>Dragging a trait changes native filter order without recreating components, supports one-step Undo/Redo, rejects other roots and persists in prefabs.</summary>
        /// <returns>An iterator that waits for GUI layout and deferred component authoring operations.</returns>
        [UnityTest]
        public IEnumerator GhostInspector_DragReordersComponentsWithUndoAndPrefabPersistence()
        {
            GameObject root = new GameObject("trait drag root");
            root.SetActive(false);
            Ghost ghost = root.AddComponent<Ghost>();
            Smoothing smoothing = root.AddComponent<Smoothing>();
            smoothing.enabled = false;
            smoothing.PositionHalfLife = 0.17f;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.hideFlags = HideFlags.HideInInspector | HideFlags.DontUnloadUnusedAsset;
            PacketCorrection packet = root.AddComponent<PacketCorrection>();
            Extrapolation extrapolation = root.AddComponent<Extrapolation>();
            GameObject otherRoot = new GameObject("other trait drag root");
            otherRoot.SetActive(false);
            otherRoot.AddComponent<Ghost>();
            Smoothing other = otherRoot.AddComponent<Smoothing>();
            UnityEditor.Editor inspector = UnityEditor.Editor.CreateEditor(ghost);
            InspectorWindow window = ScriptableObject.CreateInstance<InspectorWindow>();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/EmasTraitReordering.prefab");
            GameObject contents = null;
            try
            {
                window.Inspector = inspector;
                window.position = new Rect(100, 100, 500, 700);
                window.ShowUtility();
                window.Focus();
                for (int frame = 0; frame < 20 && window.TraitsRect.height < 30; frame++)
                {
                    window.Repaint();
                    window.SendEvent(new Event { type = EventType.Layout });
                    window.SendEvent(new Event { type = EventType.Repaint });
                    yield return null;
                }

                Assert.That(window.TraitsRect.height, Is.GreaterThan(30), "The actual Ghost Inspector must lay out its trait cards.");
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { smoothing, packet, extrapolation }));
                Component[] fixedComponents = Array.FindAll(root.GetComponents<Component>(), value => !(value is Trait));
                Drop(window, smoothing, false);
                Assert.That(ghost.Traits[0], Is.SameAs(smoothing), "Component moves must wait until the GUI pass is complete.");
                for (int frame = 0; frame < 20 && ghost.Traits[0] == smoothing; frame++)
                {
                    yield return null;
                }

                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { packet, extrapolation, smoothing }));
                Assert.That(root.GetComponents<Filter>(), Is.EqualTo(new Filter[] { packet, smoothing }),
                    "The visible card order must change the runtime filter order, not only an Inspector list.");
                Assert.That(Array.FindAll(root.GetComponents<Component>(), value => !(value is Trait)), Is.EqualTo(fixedComponents));
                Assert.That(root.GetComponent<BoxCollider>(), Is.SameAs(collider));
                Assert.That(collider.hideFlags, Is.EqualTo(HideFlags.HideInInspector | HideFlags.DontUnloadUnusedAsset));
                Assert.That(smoothing.enabled, Is.False);
                Assert.That(smoothing.PositionHalfLife, Is.EqualTo(0.17f));
                Undo.PerformUndo();
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { smoothing, packet, extrapolation }));
                Undo.PerformRedo();
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { packet, extrapolation, smoothing }));

                Drop(window, smoothing, true);
                for (int frame = 0; frame < 20 && ghost.Traits[0] != smoothing; frame++)
                {
                    yield return null;
                }

                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { smoothing, packet, extrapolation }));
                Undo.PerformUndo();
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { packet, extrapolation, smoothing }));
                Assert.That(collider.hideFlags, Is.EqualTo(HideFlags.HideInInspector | HideFlags.DontUnloadUnusedAsset));

                // A component dragged from another root cannot migrate into this Ghost.
                Drop(window, other, false);
                yield return null;
                Assert.That(ghost.Traits, Is.EqualTo(new Trait[] { packet, extrapolation, smoothing }));
                Assert.That(other.gameObject, Is.SameAs(otherRoot));
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, prefabPath), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                Assert.That(contents.GetComponents<Trait>(), Is.EqualTo(new Trait[]
                {
                    contents.GetComponent<PacketCorrection>(), contents.GetComponent<Extrapolation>(), contents.GetComponent<Smoothing>()
                }));
                Assert.That(contents.GetComponent<Smoothing>().enabled, Is.False);
                Assert.That(contents.GetComponent<Smoothing>().PositionHalfLife, Is.EqualTo(0.17f));
            }
            finally
            {
                if (contents != null)
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                window.Close();
                UnityEngine.Object.DestroyImmediate(inspector);
                Undo.ClearUndo(root);
                Undo.ClearUndo(smoothing);
                Undo.ClearUndo(packet);
                Undo.ClearUndo(extrapolation);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(otherRoot);
                AssetDatabase.DeleteAsset(prefabPath);
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            }
        }

        private static void Drop(InspectorWindow window, Trait source, bool first)
        {
            Vector2 point = new Vector2(window.TraitsRect.center.x, first ? window.TraitsRect.yMin + 2f : window.TraitsRect.yMax - 2f);
            window.DragSource = source;
            window.SendEvent(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 });
            window.SendEvent(new Event { type = EventType.MouseDrag, mousePosition = point, button = 0 });
            window.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = point });
            window.SendEvent(new Event { type = EventType.DragPerform, mousePosition = point });
            window.SendEvent(new Event { type = EventType.DragExited, mousePosition = point });
            window.DragSource = null;
        }
    }
}
