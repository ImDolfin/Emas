using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Animates a manifested vehicle view from its ghost articulation data.</summary>

    public sealed class ArticulationLogic : MonoBehaviour
    {
        private View _view;
        private IArticulate _articulation;

        private void Update()
        {
            if (!TryGetArticulation())
            {
                return;
            }

            transform.localRotation = Quaternion.Euler(
                0.0f,
                _articulation.Steering * 12.0f,
                0.0f);
        }

        private bool TryGetArticulation()
        {
            if (_view == null)
            {
                _view = GetComponent<View>();
            }

            if (_view == null || _view.Ghost == null)
            {
                return false;
            }

            return _view.Ghost.TryGet<IArticulate>(out _articulation);
        }
    }
}
