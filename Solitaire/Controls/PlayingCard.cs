using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;

namespace Solitaire.Controls;

public class PlayingCard : TemplatedControl
{
    public static readonly new RoutedEvent<RoutedEventArgs> DoubleTappedEvent =
        RoutedEvent.Register<PlayingCard, RoutedEventArgs>(nameof(DoubleTapped), RoutingStrategies.Bubble);

    public new event EventHandler<RoutedEventArgs> DoubleTapped
    {
        add => AddHandler(DoubleTappedEvent, value);
        remove => RemoveHandler(DoubleTappedEvent, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> SingleTappedEvent =
        RoutedEvent.Register<PlayingCard, RoutedEventArgs>(nameof(SingleTapped), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> SingleTapped
    {
        add => AddHandler(SingleTappedEvent, value);
        remove => RemoveHandler(SingleTappedEvent, value);
    }

    private DateTime _lastTapTime = DateTime.MinValue;
    private bool _isProcessingDoubleTap = false;
    private const int DoubleTapThresholdMs = 300; // 300ms threshold for double-tap

    /// <summary>
    /// Updates the visual selection state of the card.
    /// </summary>
    /// <param name="isSelected">Whether the card should appear selected.</param>
    public void UpdateSelectionState(bool isSelected)
    {
        if (isSelected)
        {
            Classes.Add("selected");
        }
        else
        {
            Classes.Remove("selected");
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        
        System.Diagnostics.Debug.WriteLine($"OnPointerPressed: Button={e.GetCurrentPoint(this).Properties.PointerUpdateKind}, IsLeftButton={e.GetCurrentPoint(this).Properties.IsLeftButtonPressed}");
        
        // Only handle left button presses
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            System.Diagnostics.Debug.WriteLine("OnPointerPressed: Not left button, ignoring");
            return;
        }
        
        // Prevent multiple double-tap events from being processed
        if (_isProcessingDoubleTap)
        {
            System.Diagnostics.Debug.WriteLine("OnPointerPressed: Already processing double-tap, ignoring");
            return;
        }
        
        var currentTime = DateTime.Now;
        var timeSinceLastTap = (currentTime - _lastTapTime).TotalMilliseconds;
        
        System.Diagnostics.Debug.WriteLine($"OnPointerPressed: Time since last tap: {timeSinceLastTap}ms");
        
        if (timeSinceLastTap <= DoubleTapThresholdMs)
        {
            // This is a double-tap
            System.Diagnostics.Debug.WriteLine("OnPointerPressed: Double-tap detected");
            _isProcessingDoubleTap = true;
            RaiseEvent(new RoutedEventArgs(DoubleTappedEvent));
            _lastTapTime = DateTime.MinValue; // Reset to prevent triple-tap
            
            // Reset the processing flag after a short delay
            Dispatcher.UIThread.Post(() => _isProcessingDoubleTap = false, DispatcherPriority.Background);
        }
        else
        {
            // This is a single-tap
            System.Diagnostics.Debug.WriteLine("OnPointerPressed: Single-tap detected, raising event immediately");
            _lastTapTime = currentTime;
            
            // Raise single-tap event immediately instead of with delay
            RaiseEvent(new RoutedEventArgs(SingleTappedEvent));
        }
    }
}