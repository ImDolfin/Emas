using UnityEngine;

namespace Emas
{
    // Unity rejects ancestor activation changes while a descendant callback is running.
    // Update the entity lookup immediately. Queue scene changes from callbacks until this change finishes.
    internal sealed class SceneChangeQueue
    {
        private readonly struct Change
        {
            internal readonly GameObject Target;
            internal readonly bool Active;
            internal readonly bool Destroy;

            internal Change(GameObject target, bool active, bool destroy)
            {
                Target = target;
                Active = active;
                Destroy = destroy;
            }
        }

        private readonly CommandQueue<Change> _pending = new CommandQueue<Change>(Execute);

        internal void SetActive(GameObject target, bool active)
        {
            Apply(new Change(target, active, false));
        }

        internal void Destroy(GameObject target)
        {
            Apply(new Change(target, false, true));
        }

        private void Apply(Change change)
        {
            _pending.Enqueue(change);
            _pending.ExecuteAll();
        }

        private static void Execute(Change change)
        {
            if (change.Target == null)
            {
                return;
            }

            // Disable before destroying so cleanup callbacks run through the same scene-change queue.
            if (change.Target.activeSelf != change.Active)
            {
                change.Target.SetActive(change.Active);
            }

            if (change.Destroy && change.Target != null)
            {
                Object.Destroy(change.Target);
            }
        }
    }
}
