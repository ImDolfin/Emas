using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Applies a small view-only motion effect to a manifested vehicle.</summary>

    public sealed class VehicleLogic : MonoBehaviour
    {
        private View _view;
        private I3DPosition _position;

        private void Update()
        {
            if (!TryGetPosition())
            {
                return;
            }

            var offset = Mathf.Sin(Time.time * 5.0f + _position.Position.x) * 0.035f;
            transform.localPosition = new Vector3(0.0f, offset, 0.0f);
        }

        private bool TryGetPosition()
        {
            if (_view == null)
            {
                _view = GetComponent<View>();
            }

            if (_view == null || _view.Ghost == null)
            {
                return false;
            }

            return _view.Ghost.TryGet<I3DPosition>(out _position);
        }
    }
}
