using System;

namespace CircuitBreaker
{
    public interface ICircuitBreaker
    {
        /// <summary>
        ///     Returns a boolean indicating whether the circuit breaker is in a Closed state.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        ///     Returns a boolean indicating whether the circuit breaker is in an Open state.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        ///     Returns a boolean indicating whether the circuit breaker is in a Half Open state.
        /// </summary>
        bool IsHalfOpen { get; }

        /// <summary>
        ///     Attempt to invoke arbitrary code protected by the Circuit Breaker.
        /// </summary>
        /// <param name="protectedCode"></param>
        void AttemptCall(Action protectedCode);

        /// <summary>
        ///     Manually close the circuit.
        /// </summary>
        void Close();

        /// <summary>
        ///     Manually open the circuit.
        /// </summary>
        void Open();
    }
}