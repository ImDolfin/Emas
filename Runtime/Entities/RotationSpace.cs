namespace Emas
{
    /// <summary>Identifies the axes used by a cached Spatial or ReferenceFrame quaternion.</summary>
    public enum RotationSpace
    {
        /// <summary>Shared Cartesian or local tangent axes selected by ReferenceFrame.Coordinates.</summary>
        Source,

        /// <summary>Local east/up/north axes, independent of ReferenceFrame.Coordinates.</summary>
        Geographic,

        /// <summary>Active source-body-to-ECEF rotation, with an explicit right-handed body-axis mapping.</summary>
        EarthCentered
    }
}
