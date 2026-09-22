using System.Collections.Generic;
using UnityEngine;

namespace Emas
{
    // Unity rejects ancestor activation changes while a descendant callback is running.
    // Registry mutations stay immediate; nested scene effects drain after the current effect returns.
    internal sealed class SceneEffects
    {
        private readonly Queue<Effect> _pending = new Queue<Effect>();
        private bool _applying;

        internal void SetActive(GameObject target, bool active)
        {
            Apply(new Effect(target, active, false));
        }

        internal void Destroy(GameObject target)
        {
            Apply(new Effect(target, false, true));
        }

        private void Apply(Effect effect)
        {
            _pending.Enqueue(effect);
            if (_applying)
            {
                return;
            }

            _applying = true;
            try
            {
                while (_pending.Count > 0)
                {
                    Effect next = _pending.Dequeue();
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

        private struct Effect
        {
            internal Effect(GameObject target, bool active, bool destroy)
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
