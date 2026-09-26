using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Unity rejects ancestor activation changes while a descendant callback is running.
    // Registry mutations stay immediate; nested scene changes drain after the current change returns.
    internal sealed class SceneChangeQueue
    {
        private readonly Queue<Change> _pending = new Queue<Change>();
        private bool _applying;

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
            if (_applying)
            {
                return;
            }

            _applying = true;
            try
            {
                while (_pending.Count > 0)
                {
                    Change next = _pending.Dequeue();
                    if (next.Target == null)
                    {
                        continue;
                    }

                    if (next.Target.activeSelf != next.Active)
                    {
                        next.Target.SetActive(next.Active);
                    }

                    if (next.Destroy && next.Target != null)
                    {
                        Object.Destroy(next.Target);
                    }
                }
            }
            finally
            {
                _applying = false;
            }
        }

        private struct Change
        {
            internal Change(GameObject target, bool active, bool destroy)
            {
                Target = target;
                Active = active;
                Destroy = destroy;
            }

            internal readonly GameObject Target;
            internal readonly bool Active;
            internal readonly bool Destroy;
        }
    }
}
