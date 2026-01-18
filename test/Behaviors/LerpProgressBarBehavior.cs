using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace test.Behaviors;

public static class LerpProgressBarBehavior
{
    public static readonly DependencyProperty TargetValueProperty =
        DependencyProperty.RegisterAttached(
            "TargetValue",
            typeof(double),
            typeof(LerpProgressBarBehavior),
            new PropertyMetadata(0d, OnTargetValueChanged)
        );

    public static void SetTargetValue(DependencyObject element, double value) =>
        element.SetValue(TargetValueProperty, value);

    public static double GetTargetValue(DependencyObject element) =>
        (double)element.GetValue(TargetValueProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(State),
            typeof(LerpProgressBarBehavior),
            new PropertyMetadata(null)
        );

    private static State GetOrCreateState(ProgressBar bar)
    {
        var state = (State?)bar.GetValue(StateProperty);
        if (state != null)
            return state;

        state = new State(bar);
        bar.SetValue(StateProperty, state);

        bar.Unloaded += (_, __) => state.Dispose();
        return state;
    }

    private static void OnTargetValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar bar)
            return;

        var state = GetOrCreateState(bar);
        state.Target = (double)e.NewValue;
        state.EnsureStarted();
    }

    private sealed class State : IDisposable
    {
        private readonly ProgressBar _bar;
        private DispatcherQueueTimer? _timer;
        private double _displayed;
        private long _lastTickMs;

        public double Target { get; set; }

        public State(ProgressBar bar)
        {
            _bar = bar;
            _displayed = bar.Value;
            Target = bar.Value;
        }

        public void EnsureStarted()
        {
            if (_timer != null)
                return;

            _lastTickMs = Environment.TickCount64;
            _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += (_, __) => Tick();
            _timer.Start();
        }

        private void Tick()
        {
            var now = Environment.TickCount64;
            var dt = Math.Clamp((now - _lastTickMs) / 1000.0, 0, 0.1);
            _lastTickMs = now;

            const double speed = 12.0;
            var alpha = 1.0 - Math.Exp(-speed * dt);

            _displayed = _displayed + ((Target - _displayed) * alpha);

            if (Math.Abs(Target - _displayed) < 0.05)
                _displayed = Target;

            _bar.Value = _displayed;

            // Stop after settling to reduce UI work.
            if (Math.Abs(Target - _displayed) < 0.001)
            {
                DisposeTimer();
            }
        }

        private void DisposeTimer()
        {
            if (_timer is null)
                return;

            _timer.Stop();
            _timer = null;
        }

        public void Dispose() => DisposeTimer();
    }
}
