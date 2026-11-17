using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Named appearances defined by the application assembly.</summary>

    public static class CarVariants
    {
        /// <summary>Identifies the small car appearance.</summary>
        public static readonly Variant SmallCar = new Variant("small-car");

        /// <summary>Identifies the large car appearance.</summary>
        public static readonly Variant LargeCar = new Variant("large-car");

        /// <summary>Identifies the truck appearance.</summary>
        public static readonly Variant Truck = new Variant("truck");
    }
}
