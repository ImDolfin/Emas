namespace Emas
{
    /// <summary>
    /// Converts one SDK data type into state on an initialized presence.
    /// </summary>
    /// <typeparam name="TData">The SDK data type accepted by this module.</typeparam>
    public abstract class EntityModule<TData> : EntityModule
    {
        /// <summary>
        /// Applies one detected SDK update to this module's presence.
        /// </summary>
        /// <param name="data">The update forwarded by a detector.</param>
        public abstract void Apply(TData data);

        internal override bool TryApply(object data)
        {
            if (!(data is TData))
            {
                return false;
            }

            Apply((TData)data);
            return true;
        }
    }
}