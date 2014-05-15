using System;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Rhino.Mocks;

namespace CircuitBreaker.Tests
{
    [TestFixture]
    public class CircuitBreakerTests
    {
        [TearDown]
        public void TearDown()
        {
            // Reset ambient context to default providers.
            TimeProvider.ResetToDefault();
        }

        private static void CallMultipleTimes(Action codeToCall, int timesToCall)
        {
            for (int i = 0; i < timesToCall; i++)
            {
                codeToCall();
            }
        }

        [Test]
        public void AttemptCallCallsProtectedCode()
        {
            // Arrange
            bool protectedCodeWasCalled = false;
            Action protectedCode = () => protectedCodeWasCalled = true;

            var circuitBreaker = new CircuitBreaker(10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Act
            circuitBreaker.AttemptCall(protectedCode);

            // Assert
            Assert.That(protectedCodeWasCalled);
        }

        [Test]
        public void ConstructorPopulatesCircuitBreakerProperties()
        {
            // Arrange
            const int threshold = 123;
            TimeSpan period = TimeSpan.FromMinutes(23);
            TimeSpan timeout = TimeSpan.FromMilliseconds(456);

            // Act
            var circuitBreaker = new CircuitBreaker(threshold, period, timeout);

            // Assert
            Assert.That(circuitBreaker.Period, Is.EqualTo(period));
            Assert.That(circuitBreaker.Threshold, Is.EqualTo(threshold));
            Assert.That(circuitBreaker.Timeout, Is.EqualTo(timeout));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorWithInvalidPeriodThrowsException(int milliseconds)
        {
            // Act
            var ex =
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new CircuitBreaker(10, TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMinutes(5)));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("period"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorWithInvalidThresholdThrowsException(int threshold)
        {
            // Act
            var ex =
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("threshold"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ConstructorWithInvalidTimeoutThrowsException(int milliseconds)
        {
            // Act
            var ex =
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new CircuitBreaker(10, TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(milliseconds)));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("timeout"));
        }

        [Test]
        public void DoesNotOpenIfThresholdReachedOutsideOfPeriod()
        {
            // Arrange
            Action protectedCode = () => { throw new ApplicationException("blah"); };

            const int threshold = 10;
            TimeSpan period = TimeSpan.FromMilliseconds(50);
            var circuitBreaker = new CircuitBreaker(threshold, period, TimeSpan.FromMinutes(5));

            // Act
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)),
                threshold - 1);
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)),
                threshold - 1);

            // Assert
            Assert.That(circuitBreaker.IsClosed);
        }

        [Test]
        public void FailureTimeIsLoggedWhenProtectedCodeFails()
        {
            // Arrange
            var now = DateTime.UtcNow;
            var mockTimeProvider = MockRepository.GenerateMock<TimeProvider>();
            mockTimeProvider.Expect(x => x.UtcNow).Return(now);
            TimeProvider.Current = mockTimeProvider;

            Action protectedCode = () => { throw new ApplicationException("blah"); };

            var circuitBreaker = new CircuitBreaker(10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Act
            Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode));

            // Assert
            circuitBreaker.Failures.ShouldAllBeEquivalentTo(new[] { now });
        }

        [Test]
        public void FailuresIsNotIncreasedWhenProtectedCodeSucceeds()
        {
            // Arrange
            Action protectedCode = () => { };

            var circuitBreaker = new CircuitBreaker(10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Act
            circuitBreaker.AttemptCall(protectedCode);

            // Assert
            Assert.That(circuitBreaker.Failures, Is.Empty);
        }

        [Test]
        public void NewCircuitBreakerIsClosed()
        {
            // Arrange

            // Act
            var circuitBreaker = new CircuitBreaker(10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Assert
            Assert.That(circuitBreaker.IsClosed);
        }

        [Test]
        public void OpensWhenThresholdIsReachedWithinPeriod()
        {
            // Arrange
            Action protectedCode = () => { throw new ApplicationException("blah"); };

            const int threshold = 10;
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Act
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)), threshold);

            // Assert
            Assert.That(circuitBreaker.IsOpen);
        }

        [Test]
        public void ThrowsOpenCircuitExceptionWhenCallIsAttemptedIfCircuitBreakerIsOpen()
        {
            // Arrange
            Action protectedCode = () => { throw new ApplicationException("blah"); };
            const int threshold = 10;
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

            // Act
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)), threshold);

            // Assert
            Assert.Throws<OpenCircuitException>(() => circuitBreaker.AttemptCall(protectedCode));
        }

        [Test]
        public void SwitchesToHalfOpenWhenTimeOutIsReachedAfterOpening()
        {
            // Arrange
            Action protectedCode = () => { throw new ApplicationException("blah"); };
            const int threshold = 10;
            var timeout = TimeSpan.FromMilliseconds(50);
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), timeout);

            // Act
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)), threshold);
            Thread.Sleep(TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.That(circuitBreaker.IsHalfOpen);
        }

        [Test]
        public void OpensIfExceptionIsThrownInProtectedCodeWhenInHalfOpenState()
        {
            // Arrange
            Action protectedCode = () => { throw new ApplicationException("blah"); };
            const int threshold = 10;
            var timeout = TimeSpan.FromMilliseconds(50);

            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), timeout);

            // Act
            CallMultipleTimes(
                () => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode)), threshold);
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(protectedCode));

            // Assert
            Assert.That(circuitBreaker.IsOpen);
        }

        [Test]
        public void ClosesIfProtectedCodeSucceedsInHalfOpenState()
        {
            // Arrange
            const int threshold = 10;
            var timeout = TimeSpan.FromMilliseconds(50);
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), timeout);

            // Act
            CallMultipleTimes(() => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(
                () => { throw new ApplicationException("blah"); })), threshold);

            Thread.Sleep(100);
            circuitBreaker.AttemptCall(() => { });

            // Assert
            Assert.That(circuitBreaker.IsClosed);
        }

        [Test]
        public void FailuresAreClearedWhenCircuitBreakerCloses()
        {
            // Arrange
            const int threshold = 10;
            var timeout = TimeSpan.FromMilliseconds(50);
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), timeout);

            // Act
            CallMultipleTimes(() => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(
                () => { throw new ApplicationException("blah"); })), threshold);

            Thread.Sleep(100);
            circuitBreaker.AttemptCall(() => { });

            // Assert
            Assert.That(circuitBreaker.Failures, Is.Empty);
        }

        [Test]
        public void CanCloseCircuitBreaker()
        {
            // Arrange
            const int threshold = 10;
            var timeout = TimeSpan.FromMilliseconds(50);
            var circuitBreaker = new CircuitBreaker(threshold, TimeSpan.FromMinutes(5), timeout);

            CallMultipleTimes(() => Assert.Throws<ApplicationException>(() => circuitBreaker.AttemptCall(
                () => { throw new ApplicationException("blah"); })), threshold);
            Assert.That(circuitBreaker.IsOpen);

            // Act
            circuitBreaker.Close();

            // Assert
            Assert.That(circuitBreaker.IsClosed);
        }

        [Test]
        public void CanOpenCircuitBreaker()
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(10, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            Assert.That(circuitBreaker.IsClosed);

            // Act
            circuitBreaker.Open();

            // Assert
            Assert.That(circuitBreaker.IsOpen);
        }
    }
}