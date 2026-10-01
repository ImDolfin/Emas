namespace Emas
{
    /// <summary>Specifies how a reference projects stored positions into its local scene.</summary>
    public enum ReferenceSpace
    {
        /// <summary>Shared Cartesian positions with configurable source axes.</summary>
        Cartesian,
        /// <summary>WGS84 Earth-centered positions projected into the reference's moving local tangent frame.</summary>
        Geographic
    }
}
