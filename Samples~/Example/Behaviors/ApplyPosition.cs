using UnityEngine;

namespace Emas.Sample
{
    /// <summary>
    /// Applies anchor-local position from a read-only root contract.
    /// </summary>

    public sealed class ApplyPosition : MonoBehaviour
    {
        private I3DPosition _position;

        private void Awake()
        {
            IGhost ghost = GetComponent<Ghost>();
            if (ghost != null)
            {
                ghost.TryGet<I3DPosition>(out _position);
            }
        }

        private void LateUpdate()
        {
            if (_position != null)
            {
                transform.localPosition = _position.Position;
            }
        }
    }
}
