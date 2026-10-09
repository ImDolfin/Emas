using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>Applies SDK attachment state and converts forward/right/down body offsets into Unity local axes.</summary>
    [RequireComponent(typeof(Ghost), typeof(Spatial))]
    public sealed class GeoAttachmentTrait : Trait<GeoPoseReading>
    {
        /// <summary>Attaches by parent identity, including before discovery, or resumes cached absolute placement.</summary>
        /// <param name="value">A non-null SDK snapshot, including optional parent identity and local body offset.</param>
        /// <remarks>The parent is resolved in the same Anchor as this Ghost. Detaching resumes the independently supplied absolute pose.</remarks>
        public override void Apply(GeoPoseReading value)
        {
            Spatial spatial = Ghost.GetRequired<Spatial>();
            if (value.ParentId == null)
            {
                spatial.Detach();
            }
            else
            {
                Key parent = new Key(Ghost.Key.AnchorId, value.ParentKind, value.ParentId);
                Vector3 offset = value.BodyOffset;
                spatial.Attach(parent, new Vector3(offset.y, -offset.z, offset.x));
            }
        }
    }
}
