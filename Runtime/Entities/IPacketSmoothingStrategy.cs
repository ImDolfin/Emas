namespace Emas
{
    /// <summary>Opts an extrapolation strategy into cubic pose correction when observations arrive.</summary>
    public interface IPacketSmoothingStrategy
    {
        /// <summary>Gets the duration of a packet correction in unscaled seconds.</summary>
        /// <value>A finite, nonnegative duration. Zero applies corrections immediately. Each root owns its own spline state.</value>
        float PacketSmoothingDuration { get; }
    }
}
