using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Scrolls authored road markings by the followed car's actual travel distance.</summary>
    public sealed class RoadMotion : MonoBehaviour
    {
        [SerializeField]
        private RealmSetup _setup;
        [Tooltip("Authored road markings, spaced five metres apart along Z.")]
        [SerializeField]
        private Transform[] _markings;
        private Vector3[] _positions;
        private ReferenceFrame _previousFrame;
        private Double3 _previousPosition;
        private double _distance;

        private void Awake()
        {
            _positions = new Vector3[_markings.Length];
            for (int index = 0; index < _markings.Length; index++)
            {
                _positions[index] = _markings[index].localPosition;
            }
        }

        private void LateUpdate()
        {
            ReferenceFrame frame = _setup.Realm == null ? null : _setup.Realm.ReferenceFrame;
            if (frame != null && frame.HasPosition)
            {
                if (ReferenceEquals(frame, _previousFrame))
                {
                    _distance += Double3.Distance(_previousPosition, frame.Position);
                }
                else
                {
                    // A restarted realm supplies a new frame; do not count its origin reset as travelled distance.
                    _distance = 0;
                    _previousFrame = frame;
                }
                _previousPosition = frame.Position;
            }
            // Wrap at the authored marking spacing to keep local offsets small during long runs.
            float offset = (float)(_distance % 5.0);
            for (int index = 0; index < _markings.Length; index++)
            {
                _markings[index].localPosition = _positions[index] - Vector3.forward * offset;
            }
        }
    }
}
