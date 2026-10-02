using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Applies SDK attachment state and converts forward/right/down body offsets into Unity local axes.</summary>
    [RequireComponent(typeof(Ghost), typeof(Spatial))]
    public sealed class GeoAttachmentModule : EntityModule<GeoPoseReading>
    {
        /// <summary>Attaches by parent identity, including before discovery, or resumes cached absolute placement.</summary>
        /// <param name="value">The latest SDK snapshot, including optional parent identity and local body offset.</param>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = GetComponent<Spatial>();
            if (value.ParentId == null)
            {
                spatial.Detach();
            }
            else
            {
                Key parent = new Key(GetComponent<Ghost>().Key.AnchorId, value.ParentKind, value.ParentId);
                Vector3 offset = value.BodyOffset;
                spatial.Attach(parent, new Vector3(offset.y, -offset.z, offset.x));
            }
        }
    }
}
