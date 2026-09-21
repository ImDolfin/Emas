using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Applies a root position interface to the Unity transform.</summary>

    public sealed class ApplyPosition : MonoBehaviour
    {
        private I3DPosition _position;

        private void Awake()
        {
            var components = GetComponents<MonoBehaviour>();
            for (var index = 0; index < components.Length; index++)
            {
                _position = components[index] as I3DPosition;
                if (_position != null)
                {
                    break;
                }
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
