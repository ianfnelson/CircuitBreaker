using System;
using System.Collections.Generic;

namespace CircuitBreaker
{
	public class CircuitBreaker : ICircuitBreaker
	{
	    private readonly List<DateTime> failures; 
		private readonly object monitor = new object();
		private CircuitBreakerState state;

		public CircuitBreaker(int threshold, TimeSpan period, TimeSpan timeout)
		{
			if (threshold < 1)
			{
				throw new ArgumentOutOfRangeException("threshold", "Threshold should be greater than 0");
			}

            if (period.TotalMilliseconds < 1)
            {
                throw new ArgumentOutOfRangeException("period", "Period should be greater than 0");
            }

			if (timeout.TotalMilliseconds < 1)
			{
				throw new ArgumentOutOfRangeException("timeout", "Timeout should be greater than 0");
			}

			Threshold = threshold;
		    Period = period;
			Timeout = timeout;
            failures = new List<DateTime>();
			MoveToClosedState();
		}

        public IList<DateTime> Failures
        {
            get
            {
                // Remove log of any failed invocations that occurred earlier than period in which we are interested.
                failures.RemoveAll(f => f < TimeProvider.Current.UtcNow - Period);

                return failures;
            }
        }

		public int Threshold { get; private set; }
        public TimeSpan Period { get; private set; }
		public TimeSpan Timeout { get; private set; }

        internal bool ThresholdReached
        {
            get
            {
                return Failures.Count >= Threshold;
            }
        }

		public bool IsClosed
		{
			get { return state is ClosedState; }
		}

		public bool IsOpen
		{
			get { return state is OpenState; }
		}

		public bool IsHalfOpen
		{
			get { return state is HalfOpenState; }
		}

		internal void MoveToClosedState()
		{
			state = new ClosedState(this);
		}

		internal void MoveToOpenState()
		{
			state = new OpenState(this);
		}

		internal void MoveToHalfOpenState()
		{
			state = new HalfOpenState(this);
		}

		internal void RecordFailure()
		{
            failures.Add(TimeProvider.Current.UtcNow);
		}

		internal void ResetFailures()
		{
            failures.Clear();
		}

		public void AttemptCall(Action protectedCode)
		{
			using (TimedLock.Lock(monitor)) 
			{
				state.ProtectedCodeIsAboutToBeCalled();
			}

			try
			{
				protectedCode();
			}
			catch (Exception e)
			{
				using (TimedLock.Lock(monitor))
				{
					state.ActUponException(e);
				}
				throw;
			}

			using (TimedLock.Lock(monitor))
			{
				state.ProtectedCodeHasBeenCalled();
			}
		}

		public void Close()
		{
			using (TimedLock.Lock(monitor))
			{
			    ResetFailures();
				MoveToClosedState();
			}
		}

		public void Open()
		{
			using (TimedLock.Lock(monitor))
			{
				MoveToOpenState();
			}
		}
	}
}