using System;
using UnityEngine;

namespace Emas.Tests
{
    /// <summary>Stores a mapped value used to verify initialization and failure context.</summary>
    public sealed class TextTrait : Trait<string>
    {
        internal string Value;

        /// <summary>Stores the value supplied by the configured reader.</summary>
        /// <param name="data">The text value supplied by the bound reader.</param>
        public override void Apply(string data)
        {
            Value = data;
        }
    }
}
