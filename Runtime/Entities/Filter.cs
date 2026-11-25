using System;
using UnityEngine;

namespace Emas
{
    /// <summary>Base trait for filtering source observations before a Ghost's single prediction call.</summary>
    /// <remarks>Enabled filters run on the main thread in Inspector component order after all input traits.
    /// Attached and distant Ghosts bypass this stage. Each instance owns its estimator state. Do not write
    /// back to Spatial or project into the future here; retain timestamps or declare the estimate's actual age.</remarks>
    [RequireComponent(typeof(Spatial))]
    public abstract class Filter : Trait
    {
        private double? _time;
        private int _resetVersion;

        internal sealed override bool NeedsRefresh => false;

        internal int SettingsVersion => unchecked(ConfigurationVersion * 397 ^ _resetVersion);

        /// <summary>Gets a value that changes when this filter's configuration changes.</summary>
        /// <value>A configuration hash for live presentation transitions; the default is zero.</value>
        protected virtual int ConfigurationVersion => 0;

        /// <summary>Discards this filter's state, retaining raw SDK inputs and component configuration.</summary>
        /// <remarks>The next filter evaluation starts from its input. Call after an estimator discontinuity.</remarks>
        public void Reset()
        {
            ClearState();
            _resetVersion++;
            Spatial spatial = GetComponent<Spatial>();
            if (spatial != null)
            {
                spatial.ResetTuning();
            }
        }

        internal FilterState Apply(FilterState input, Spatial spatial, CoordinateSystem axes, double timestamp)
        {
            double elapsed = _time.HasValue ? Math.Max(0d, timestamp - _time.Value) : 0;
            FilterState result = Process(input, new FilterContext(spatial, axes, timestamp, elapsed));
            result.Validate();
            _time = timestamp;
            return result;
        }

        internal void ClearState()
        {
            _time = null;
            OnReset();
        }

        internal void Deactivate()
        {
            if (_time.HasValue)
            {
                ClearState();
            }
        }

        internal sealed override void Refresh()
        {
            // Observation filters run in the projection stage, after all bound readers.
        }

        internal sealed override void ClearBinding()
        {
            Reset();
        }

        /// <summary>Filters one source-state copy before the next filter and extrapolation.</summary>
        /// <param name="input">The preceding filter's estimate, or accepted Spatial input for the first filter.</param>
        /// <param name="context">Local processing time and source coordinate conventions.</param>
        /// <returns>A finite source-state estimate with coherent pose, motion and SDK timestamps.</returns>
        /// <remarks>Called for each new timestamp group, or once for a held-input update. Preserve unavailable channels unless your
        /// estimator supplies them. History fitting uses these outputs, independently of raw input storage.</remarks>
        protected abstract FilterState Process(FilterState input, FilterContext context);

        /// <summary>Clears estimator history after Reset, attachment, range bypass recovery or a clock/frame reset.</summary>
        protected virtual void OnReset()
        {
        }
    }
}
