using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Solitaire.Controls;
using Solitaire.ViewModels;
using Solitaire.ViewModels.Pages;
using System.Threading.Tasks;
using Solitaire.Utils;

namespace Solitaire.Behaviors;

public class CardFieldBehavior : Behavior<Canvas>
{
    public static readonly AttachedProperty<List<CardStackPlacementControl>> CardStacksProperty =
        AvaloniaProperty.RegisterAttached<CardFieldBehavior, Control, List<CardStackPlacementControl>>(
            "CardStacks", inherits: true);

    public static void SetCardStacks(Control obj, List<CardStackPlacementControl> value) =>
        obj.SetValue(CardStacksProperty, value);

    public static List<CardStackPlacementControl> GetCardStacks(Control obj) => obj.GetValue(CardStacksProperty);


    private readonly Dictionary<PlayingCardViewModel, Control> _containerCache = new();
    private List<Control> _draggingContainers = new();
    private List<PlayingCardViewModel> _draggingCards = new();
    private List<int> _startZIndices = new();
    private List<Point> _homePoints = new();
    private bool _isDragging;
    private CardStackPlacementControl? _homeStack;
    private Point _startPoint;
    
    // FIX: New fields for proper drag detection
    private PlayingCardViewModel? _potentialDragCard;
    private CardStackPlacementControl? _potentialDragStack;
    private Point _potentialDragStartPoint;
    
    // Additional fields for drag and keyboard operations
    private List<Control>? _keyMoveContainers;
    private List<PlayingCardViewModel>? _keyboardMoveCards;
    private bool _keyboardMove;

    // FIX: Add cooldown to prevent immediate re-dragging after auto-move
    private readonly HashSet<PlayingCardViewModel> _recentlyAutoMovedCards = new();
    private readonly Dictionary<PlayingCardViewModel, DateTime> _autoMoveCooldowns = new();


    private static readonly AttachedProperty<Vector?> HomePositionProperty =
        AvaloniaProperty.RegisterAttached<CardFieldBehavior, AvaloniaObject, Vector?>(
            "HomePosition");

    private static void SetHomePosition(AvaloniaObject obj, Vector? value) =>
        obj.SetValue(HomePositionProperty, value);

    private static Vector? GetHomePosition(AvaloniaObject obj) => obj.GetValue(HomePositionProperty);

    /// <inheritdoc />
    protected override void OnAttached()
    {
        if (AssociatedObject == null) return;
        AssociatedObject.Background = Brushes.Transparent;
        AssociatedObject.AttachedToVisualTree += AssociatedObjectOnAttachedToVisualTree;
        AssociatedObject.DetachedFromVisualTree += AssociatedObjectOnDetachedFromVisualTree;
        AssociatedObject.KeyDown += AssociatedObjectOnKeyDown;
        AssociatedObject.PointerPressed += AssociatedObjectOnPointerPressed;
        AssociatedObject.PointerMoved += AssociatedObjectOnPointerMoved;
        AssociatedObject.PointerReleased += AssociatedObjectOnPointerReleased;
        AssociatedObject.PointerCaptureLost += AssociatedObjectOnPointerCaptureLost;
        base.OnAttached();
    }

    private void AssociatedObjectOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetDragAndKeyMove();
    }

    private void AssociatedObjectOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || _draggingContainers is null || _draggingCards is null) return;

        var absCur = e.GetCurrentPoint(TopLevel.GetTopLevel(AssociatedObject));
        var absCurPos = absCur.Position;

        if (AssociatedObject == null) return;

        foreach (var visual in TopLevel.GetTopLevel(AssociatedObject)!.GetVisualsAt(absCurPos)
                     .OrderByDescending(x => x.ZIndex))
        {
            if (visual is not Border { DataContext: CardGameViewModel game } border
                || border.FindAncestorOfType<CardStackPlacementControl>() is not { } toStack) continue;

            var cardStacks = GetCardStacks(_draggingContainers![0]);
            var fromStack =
                cardStacks.FirstOrDefault(x => x.SourceItems != null && x.SourceItems.Contains(_draggingCards[0]));

            // Trigger on different stack.
            if (fromStack?.SourceItems != null && toStack.SourceItems != null &&
                !fromStack.SourceItems.SequenceEqual(toStack.SourceItems))
            {
                // Save reference to current card before resetting. 
                var targetCard = _draggingCards[0];
                var validMove = game.CheckAndMoveCard(fromStack.SourceItems, toStack.SourceItems, targetCard);

                ResetDragAndKeyMove(!validMove);
            }

            break;
        }

        // Handle card selection move attempt when dropping on a target stack
        if (AssociatedObject != null)
        {
            var dropPos = e.GetCurrentPoint(AssociatedObject).Position;
            foreach (var visual in TopLevel.GetTopLevel(AssociatedObject)!.GetVisualsAt(dropPos)
                         .OrderByDescending(x => x.ZIndex))
            {
                if (visual is Border { DataContext: CardGameViewModel game } border
                    && border.FindAncestorOfType<CardStackPlacementControl>() is { } targetStack)
                {
                    // Try to move the selected card to the target stack
                    if (game is KlondikeSolitaireViewModel klondikeGame && klondikeGame.SelectedCard != null)
                    {
                        var sourceCollection = klondikeGame.GetCardCollection(klondikeGame.SelectedCard);
                        if (sourceCollection != null && targetStack.SourceItems != null && !sourceCollection.SequenceEqual(targetStack.SourceItems))
                        {
                            klondikeGame.TryMoveSelectedCardTo(targetStack.SourceItems);
                        }
                    }
                    break;
                }
            }
        }

        ResetDragAndKeyMove();
    }

    private bool ResetDragAndKeyMove(bool returnHome = true)
    {
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: ResetDragAndKeyMove called - returnHome: {returnHome} !!!");
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Before reset - _isDragging: {_isDragging}, _draggingContainers: {_draggingContainers?.Count ?? 0}, _homeStack: {_homeStack?.Name ?? "NULL"} !!!");
        
        if (!_isDragging && !_keyboardMove) return false;

        if (_draggingContainers is not null)
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Resetting dragging containers !!!");
            foreach (var pair in _draggingContainers.Select((container, i) => (container, i)))
            {
                pair.container.Classes.Remove("dragging");

                if (!returnHome || _homePoints is null || _startZIndices is null) continue;

                SetCanvasPosition(pair.container, _homePoints[pair.i]);
                pair.container.ZIndex = _startZIndices[pair.i];
            }
        }
        
        foreach (var cardStack in GetCardStacks(AssociatedObject!))
        {
            cardStack.ClearValue(InputElement.FocusableProperty);
        }
        
        AssociatedObject!.IsEnabled = true;

        _keyMoveContainers?.LastOrDefault()?.Focus(NavigationMethod.Directional);

        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Resetting drag state variables !!!");
        _isDragging = false;
        _draggingCards = null;
        _draggingContainers = null;
        _startZIndices = null;
        _startPoint = new Point();
        
        // FIX: Clear potential drag fields
        _potentialDragCard = null;
        _potentialDragStack = null;
        _potentialDragStartPoint = new Point();
        
        // FIX: Clear home stack reference to prevent state corruption
        _homeStack = null;
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: After reset - _isDragging: {_isDragging}, _draggingContainers: {_draggingContainers?.Count ?? 0}, _homeStack: {_homeStack?.Name ?? "NULL"} !!!");

        _keyboardMove = false;
        _keyboardMoveCards = null;
        _keyMoveContainers = null;
        
        return true;
    }

    private void AssociatedObjectOnPointerMoved(object? sender, PointerEventArgs e)
    {
        // FIX: New drag detection logic - only prepare for drag if there's actual movement
        if (_potentialDragCard != null && _potentialDragStack != null && !_isDragging)
        {
            // FIX: Check if card is in cooldown after auto-move
            if (_recentlyAutoMovedCards.Contains(_potentialDragCard))
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card {_potentialDragCard.CardType} in cooldown after auto-move - skipping drag detection !!!");
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Cooldown check hit in drag detection at: {DateTime.Now:HH:mm:ss.fff} !!!");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Starting drag detection logic !!!");
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Current drag state - _isDragging: {_isDragging}, _draggingContainers: {_draggingContainers?.Count ?? 0}, _homeStack: {_homeStack?.Name ?? "NULL"} !!!");
                
                // FIX: Ensure _potentialDragStartPoint is properly initialized
                if (_potentialDragStartPoint == default(Point))
                {
                    System.Diagnostics.Debug.WriteLine($"!!! WARNING: _potentialDragStartPoint not initialized !!!");
                    _potentialDragStartPoint = e.GetCurrentPoint(_potentialDragStack).Position;
                }
                
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Getting current point !!!");
                var potentialDragCurrentPoint = e.GetCurrentPoint(_potentialDragStack);
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Current point obtained: {potentialDragCurrentPoint.Position} !!!");
                
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Calculating delta !!!");
                var potentialDragDelta = potentialDragCurrentPoint.Position - _potentialDragStartPoint;
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Delta calculated: {potentialDragDelta} !!!");
                
                // Check if there's significant movement to trigger actual drag
                var potentialDragMovementThreshold = 5.0; // 5 pixels minimum movement
                var hasPotentialDragMovement = Math.Abs(potentialDragDelta.X) > potentialDragMovementThreshold || Math.Abs(potentialDragDelta.Y) > potentialDragMovementThreshold;
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Movement threshold check: {hasPotentialDragMovement} !!!");
                
                if (hasPotentialDragMovement)
                {
                    System.Diagnostics.Debug.WriteLine($"!!! ACTUAL DRAG DETECTED !!!");
                    System.Diagnostics.Debug.WriteLine($"Card: {_potentialDragCard.CardType}, Movement: {potentialDragDelta.X:F1}, {potentialDragDelta.Y:F1}");
                    System.Diagnostics.Debug.WriteLine($"Time: {DateTime.Now:HH:mm:ss.fff}");
                    
                    // DEBUG: Check parameters before calling PrepareForDrag
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: PrepareForDrag parameters !!!");
                    System.Diagnostics.Debug.WriteLine($"Card: {_potentialDragCard?.CardType.ToString() ?? "NULL"}");
                    System.Diagnostics.Debug.WriteLine($"Stack: {_potentialDragStack?.Name ?? "NULL"}");
                    System.Diagnostics.Debug.WriteLine($"EventArgs: {e != null}");
                    System.Diagnostics.Debug.WriteLine($"Card null: {_potentialDragCard == null}");
                    System.Diagnostics.Debug.WriteLine($"Stack null: {_potentialDragStack == null}");
                    
                    // FIX: Safety check to prevent null reference exceptions
                    if (_potentialDragCard != null && _potentialDragStack != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: About to call PrepareForDrag !!!");
                        // Now prepare for actual drag
                        PrepareForDrag(_potentialDragCard, _potentialDragStack, e);
                        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: PrepareForDrag completed successfully !!!");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"!!! ERROR: Cannot prepare for drag - null parameters detected !!!");
                    }
                    
                    // Clear potential drag info
                    _potentialDragCard = null;
                    _potentialDragStack = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"!!! ERROR in drag detection: {ex.Message} !!!");
                System.Diagnostics.Debug.WriteLine($"!!! ERROR stack trace: {ex.StackTrace} !!!");
                // Clear potential drag info on error to prevent further crashes
                _potentialDragCard = null;
                _potentialDragStack = null;
            }
        }
        
        // Handle actual dragging
        if (!_isDragging || _draggingContainers == null || _homePoints == null || _homeStack == null) return;

        if (!Equals(e.Pointer.Captured, _draggingContainers[0])) return;

        var currentPoint = e.GetCurrentPoint(_homeStack);
        var delta = currentPoint.Position - _startPoint;
        
        // FIX: Only set drag Z-index if there's significant movement (prevents conflicts during tiny movements)
        var movementThreshold = 5.0; // 5 pixels minimum movement
        var hasSignificantMovement = Math.Abs(delta.X) > movementThreshold || Math.Abs(delta.Y) > movementThreshold;
        
        if (hasSignificantMovement)
        {
            // FIX: Set drag Z-index only when actually moving (not just on pointer press)
            // This prevents Z-index conflicts during normal card interactions
            foreach (var draggingContainer in _draggingContainers.Select((control, i) => (control, i)))
            {
                var dragZIndex = 2100000000 + draggingContainer.i; // Same high value as in PrepareForDrag
                draggingContainer.control.ZIndex = dragZIndex;
                
                System.Diagnostics.Debug.WriteLine($"!!! DRAG Z-INDEX SET ON MOVEMENT !!!");
                if (_draggingCards != null && draggingContainer.i < _draggingCards.Count)
                {
                    System.Diagnostics.Debug.WriteLine($"Card: {_draggingCards[draggingContainer.i].CardType}, Drag Z-Index: {dragZIndex}");
                }
                System.Diagnostics.Debug.WriteLine($"Movement: {delta.X:F1}, {delta.Y:F1}");
            }
        }
        
        foreach (var draggingContainer in _draggingContainers.Select((control, i) => (control, i)))
        {
            if (_homePoints == null) continue;
            
            SetCanvasPosition(draggingContainer.control, _homePoints[draggingContainer.i] + delta);
        }
    }

    /// <summary>
    /// Prepares for actual drag operation when movement is detected.
    /// This prevents Z-index conflicts during normal card interactions.
    /// </summary>
    private void PrepareForDrag(PlayingCardViewModel card, CardStackPlacementControl stack, PointerEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: PrepareForDrag started !!!");
        
        if (stack.SourceItems == null) 
        {
            System.Diagnostics.Debug.WriteLine($"!!! ERROR: stack.SourceItems is null !!!");
            return;
        }
        
        // FIX: Add null checks to prevent crashes
        if (card == null || stack == null || e == null)
        {
            System.Diagnostics.Debug.WriteLine($"!!! PrepareForDrag: Null parameter detected !!!");
            System.Diagnostics.Debug.WriteLine($"Card: {card?.CardType.ToString() ?? "NULL"}, Stack: {stack?.Name ?? "NULL"}, EventArgs: {e != null}");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Parameters validated, clearing collections !!!");
        // FIX: Clear collections for new drag
        _draggingCards?.Clear();
        _draggingContainers?.Clear();
        _startZIndices?.Clear();
        _homePoints?.Clear();
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Collections cleared successfully !!!");
        
        // FIX: Initialize collections if needed
        if (_draggingCards == null) _draggingCards = new List<PlayingCardViewModel>();
        if (_draggingContainers == null) _draggingContainers = new List<Control>();
        if (_startZIndices == null) _startZIndices = new List<int>();
        if (_homePoints == null) _homePoints = new List<Point>();
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Collections initialized successfully !!!");
        
        // FIX: Get the card's index in the stack
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: About to get card index from stack !!!");
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Stack SourceItems reference: {stack.SourceItems?.GetHashCode() ?? 0} !!!");
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Stack SourceItems count: {stack.SourceItems?.Count ?? 0} !!!");
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card reference: {card.GetHashCode()} !!!");
        
        var cardIndex = stack.SourceItems.IndexOf(card);
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card index found: {cardIndex} !!!");
        
        // FIX: If the card is not found in the stack (e.g., after auto-move), abort the drag
        if (cardIndex == -1)
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card {card.CardType} not found in stack - aborting drag (likely auto-moved) !!!");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: About to iterate through cards !!!");
        foreach (var c in stack.SourceItems.Select((card2, i) => (card2, i))
                     .Where(a => a.i >= cardIndex))
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Processing card {c.card2.CardType} at index {c.i} !!!");
            
            if (!_containerCache.TryGetValue(c.card2, out var cachedContainer)) 
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Container not found for card {c.card2.CardType} !!!");
                continue;
            }
            
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Container found, adding to collections !!!");
            _draggingContainers.Add(cachedContainer);
            _draggingCards.Add(c.card2);
            _startZIndices.Add(cachedContainer.ZIndex);
            
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Getting home position !!!");
            var homePos = GetHomePosition(cachedContainer);
            _homePoints.Add(homePos.HasValue ? new Point(homePos.Value.X, homePos.Value.Y) : new Point());
            
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Adding dragging class !!!");
            cachedContainer.Classes.Add("dragging");
        }
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Finished processing cards, checking count !!!");
        if (_draggingContainers.Count == 0) 
        {
            System.Diagnostics.Debug.WriteLine($"!!! ERROR: No dragging containers prepared !!!");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Setting up drag state !!!");
        _isDragging = true;
        _homeStack = stack;
        
        // FIX: Set extremely high Z-index immediately when drag starts to ensure cards appear above others
        foreach (var draggingContainer in _draggingContainers.Select((control, i) => (control, i)))
        {
            var dragZIndex = 2100000000 + draggingContainer.i; // Much higher than base Z-index of 2000000000
            draggingContainer.control.ZIndex = dragZIndex;
            System.Diagnostics.Debug.WriteLine($"!!! DRAG Z-INDEX SET ON PREPARATION !!!");
            if (_draggingCards != null && draggingContainer.i < _draggingCards.Count)
            {
                System.Diagnostics.Debug.WriteLine($"Card: {_draggingCards[draggingContainer.i].CardType}, Drag Z-Index: {dragZIndex}");
            }
        }
        
        // FIX: Use the current pointer position instead of potentially null _potentialDragStartPoint
        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Getting current pointer position !!!");
        var currentPoint = e.GetCurrentPoint(stack);
        _startPoint = currentPoint.Position;
        
        // Capture the pointer for the first dragging container
        if (_draggingContainers.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Capturing pointer !!!");
            e.Pointer.Capture(_draggingContainers[0]);
        }
        
        System.Diagnostics.Debug.WriteLine($"!!! DRAG PREPARATION COMPLETED !!!");
        System.Diagnostics.Debug.WriteLine($"Cards prepared: {_draggingCards.Count}");
        System.Diagnostics.Debug.WriteLine($"Time: {DateTime.Now:HH:mm:ss.fff}");
    }

    private static void SetCanvasPosition(AvaloniaObject? control, Vector newVector)
    {
        if (control is null) return;

        Canvas.SetLeft(control, newVector.X);
        Canvas.SetTop(control, newVector.Y);
    }

    private void AssociatedObjectOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_isDragging) return;

        var focusedCardView = ((Control?)e.Source)?.FindAncestorOfType<PlayingCard>(true);
        var focusedPlacement = ((Control?)e.Source)?.FindAncestorOfType<CardStackPlacementControl>(true)
            ?? TopLevel.GetTopLevel(AssociatedObject)?.FocusManager!.GetFocusedElement() as CardStackPlacementControl;

        if (e.Key == Key.Space && focusedCardView is not null)
        {
            if (GetStackAndIndex(focusedCardView) is { } tuple)
            {
                _keyboardMoveCards = new List<PlayingCardViewModel>();
                _keyMoveContainers = new List<Control>();

                foreach (var c in tuple.stack.SourceItems!.Select((card2, i) => (card2, i))
                             .Where(a => a.i >= tuple.currentIndex))
                {
                    if (!_containerCache.TryGetValue(c.card2, out var cachedContainer)) continue;
                    _keyMoveContainers.Add(cachedContainer);
                    _keyboardMoveCards.Add(c.card2);
                    // _startZIndices.Add(cachedContainer.ZIndex);
                    // _homePoints.Add(GetHomePosition(cachedContainer) ?? throw new InvalidOperationException());
                    // cachedContainer.Classes.Add("dragging");
                    cachedContainer.ZIndex = int.MaxValue / 2 + c.i;
                }

                if (_keyMoveContainers.Any())
                {
                    e.Handled = _keyboardMove = true;
                    
                    foreach (var cardStack in GetCardStacks(AssociatedObject!))
                    {
                        cardStack.SetCurrentValue(InputElement.FocusableProperty, true);
                    }
                    AssociatedObject!.IsEnabled = false;

                    tuple.stack.Focus(NavigationMethod.Directional);
                }
            }
        }
        else if (e.Key == Key.Space && focusedPlacement is not null)
        {
            if (_keyboardMove && _keyboardMoveCards is not null)
            {
                var game = (CardGameViewModel)focusedPlacement.DataContext!;

                var cardStacks = GetCardStacks(_keyMoveContainers![0]);
                var fromStack = cardStacks.FirstOrDefault(x =>
                    x.SourceItems != null && x.SourceItems.Contains(_keyboardMoveCards[0]));

                if (fromStack?.SourceItems != null && focusedPlacement.SourceItems != null &&
                    !fromStack.SourceItems.SequenceEqual(focusedPlacement.SourceItems))
                {
                    // Save reference to current card before resetting. 
                    var targetCard = _keyboardMoveCards[0];
                    var validMove =
                        game.CheckAndMoveCard(fromStack.SourceItems, focusedPlacement.SourceItems, targetCard);

                    e.Handled = ResetDragAndKeyMove(!validMove);
                }
                else
                {
                    e.Handled = ResetDragAndKeyMove();
                }
            }
            else if (focusedPlacement.CommandOnCardClick?.CanExecute(null) ?? false)
            {
                focusedPlacement.CommandOnCardClick.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape && _keyboardMove)
        {
            e.Handled = ResetDragAndKeyMove();
        }
        else if (e.Key is Key.Up or Key.Down && focusedCardView is not null)
        {
            if (GetStackAndIndex(focusedCardView) is { } tuple)
            {
                var newIndex = tuple.currentIndex + (e.Key is Key.Up ? -1 : 1);
                if (tuple.stack.SourceItems!.Skip(newIndex).FirstOrDefault() is {} newCard
                    && _containerCache.TryGetValue(newCard, out var cachedContainer))
                {
                    e.Handled = cachedContainer.Focus(NavigationMethod.Directional);
                }
            }
        }
        else if (e.Key is Key.Left or Key.Right && focusedCardView is not null)
        {
            if (GetStackAndIndex(focusedCardView) is { } tuple)
            {
                var allStacks = GetCardStacks(AssociatedObject!);
                var currIndex = allStacks.IndexOf(tuple.stack);
                
                var newIndex = currIndex + (e.Key is Key.Left ? -1 : 1);
                if (allStacks.Skip(newIndex).FirstOrDefault() is {} newStack
                    && newStack.SourceItems?.LastOrDefault() is {} lastItem
                    && _containerCache.TryGetValue(lastItem, out var cachedContainer))
                {
                    e.Handled = cachedContainer.Focus(NavigationMethod.Directional);
                }
            }
        }
    }

    private static (CardStackPlacementControl stack, int currentIndex)? GetStackAndIndex(PlayingCard? sourceCardView)
    {
        if (sourceCardView is null)
            return null;

        var card = (PlayingCardViewModel)sourceCardView.DataContext!;
        var cardStacks = GetCardStacks(sourceCardView);
        var stack = cardStacks.FirstOrDefault(x => x.SourceItems != null && x.SourceItems.Contains(card));
        var currentIndex = stack?.SourceItems!.IndexOf(card);
        if (currentIndex.HasValue)
        {
            return (stack!, currentIndex.Value);
        }
        return null;
    }

    private void AssociatedObjectOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // FIX: Prevent drag detection during auto-move to avoid momentary moves
        var cardStacksForCheck = GetCardStacks(AssociatedObject!);
        var gameForCheck = cardStacksForCheck.FirstOrDefault()?.DataContext as CardGameViewModel;
        if (gameForCheck is KlondikeSolitaireViewModel klondikeGameForCheck && klondikeGameForCheck.IsAutoMoving)
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Pointer pressed blocked - auto-move in progress !!!");
            return; // Don't allow drag detection during auto-move
        }

        if (_isDragging) return;

        System.Diagnostics.Debug.WriteLine($"=== ULTRA DEBUG: OnPointerPressed START ===");
        System.Diagnostics.Debug.WriteLine($"=== Time: {DateTime.Now:HH:mm:ss.fff} ===");
        System.Diagnostics.Debug.WriteLine($"=== Cooldown count: {_recentlyAutoMovedCards.Count} ===");
        System.Diagnostics.Debug.WriteLine($"=== Cooldown cards: {string.Join(", ", _recentlyAutoMovedCards.Select(c => c.CardType))} ===");
        
        if (e.Handled) 
        {
            System.Diagnostics.Debug.WriteLine($"=== ULTRA DEBUG: Event already handled, returning ===");
            return;
        }
        
        // FIX: Only block drag preparation for specific cards in cooldown, not ALL interactions
        // This allows stock pile taps and other legitimate interactions to work
        System.Diagnostics.Debug.WriteLine($"=== Cooldown check - count: {_recentlyAutoMovedCards.Count} ===");
        if (_recentlyAutoMovedCards.Any())
        {
            System.Diagnostics.Debug.WriteLine($"=== Cooldown cards: {string.Join(", ", _recentlyAutoMovedCards.Select(c => c.CardType))} ===");
        }
        
        System.Diagnostics.Debug.WriteLine($"=== ULTRA DEBUG: No cooldown detected, proceeding with normal logic ===");

        var absCur = e.GetCurrentPoint(TopLevel.GetTopLevel(AssociatedObject));
        var absCurPos = absCur.Position;

        void ActivateCommand(CardStackPlacementControl? stack)
        {
            if (stack?.CommandOnCardClick?.CanExecute(null) ?? false)
            {
                stack.CommandOnCardClick?.Execute(null);
            }
        }

        if (!absCur.Properties.IsLeftButtonPressed || AssociatedObject == null) return;

        foreach (var visual in TopLevel.GetTopLevel(AssociatedObject)!.GetVisualsAt(absCurPos)
                     .OrderByDescending(x => x.ZIndex))
        {
            if (visual is Border { DataContext: CardGameViewModel game } border
                && border.FindAncestorOfType<CardStackPlacementControl>() is { } stack1)
            {
                System.Diagnostics.Debug.WriteLine($"=== Tapped on stack location ===");
                System.Diagnostics.Debug.WriteLine($"Stack type: {GetStackType(stack1)}");
                System.Diagnostics.Debug.WriteLine($"Stack has {stack1.SourceItems?.Count ?? 0} cards");
                
                // FIX: Handle move opportunity for selected cards to empty placement spots
                if (game is KlondikeSolitaireViewModel klondikeGame && klondikeGame.SelectedCard != null)
                {
                    System.Diagnostics.Debug.WriteLine($"=== Move opportunity to empty spot ===");
                    System.Diagnostics.Debug.WriteLine($"Selected card: {klondikeGame.SelectedCard.CardType}");
                    System.Diagnostics.Debug.WriteLine($"Target stack: {stack1.Name ?? "unnamed"}");
                    System.Diagnostics.Debug.WriteLine($"Target has {stack1.SourceItems?.Count ?? 0} cards");
                    
                    // Try to move the selected card to this empty spot
                    var targetCollection = stack1.SourceItems ?? new BatchObservableCollection<PlayingCardViewModel>();
                    var success = klondikeGame.TryMoveSelectedCardTo(targetCollection);
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Move to empty spot successful!");
                        e.Handled = true;
                        return; // Don't proceed with other logic
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Move to empty spot failed");
                    }
                }
                
                // Move opportunity logic is now handled in OnCardSingleTapped
                // This prevents conflicts and ensures proper selection state management
                
                ActivateCommand(stack1);
                break;
            }

            if (visual is not Border { DataContext: PlayingCardViewModel card } container) continue;

            var cardStacks = GetCardStacks(container);

            var stack2 =
                cardStacks.FirstOrDefault(x => x.SourceItems != null && x.SourceItems.Contains(card));

            if (stack2 is null) return;

            System.Diagnostics.Debug.WriteLine($"=== Stack detection debugging ===");
            System.Diagnostics.Debug.WriteLine($"Tapped card: {card.CardType}");
            System.Diagnostics.Debug.WriteLine($"Found stack: {stack2.Name ?? "unnamed"}");
            System.Diagnostics.Debug.WriteLine($"Stack SourceItems reference: {stack2.SourceItems?.GetHashCode()}");
            System.Diagnostics.Debug.WriteLine($"Stack SourceItems count: {stack2.SourceItems?.Count ?? 0}");
            
            // Move opportunity logic is now handled in OnCardSingleTapped
            // This prevents conflicts and ensures proper selection state management

            ActivateCommand(stack2);

            // Handle card selection for single-tap (only if no move was attempted)
            // Note: Card selection is now handled in the move opportunity section above
            // This prevents the selection from being changed before move logic runs

            // FIX: Proper drag detection - only prepare for drag if we're actually going to drag
            // Don't trigger drag preparation on every tap - this causes Z-index conflicts
            // Instead, just mark the card as potentially draggable and wait for actual movement
            
            if (card.IsPlayable && !_isDragging)
            {
                System.Diagnostics.Debug.WriteLine($"=== ULTRA DEBUG: Checking card {card.CardType} for drag marking ===");
                System.Diagnostics.Debug.WriteLine($"=== Card is playable: {card.IsPlayable} ===");
                System.Diagnostics.Debug.WriteLine($"=== Is dragging: {_isDragging} ===");
                System.Diagnostics.Debug.WriteLine($"=== Cooldown count: {_recentlyAutoMovedCards.Count} ===");
                System.Diagnostics.Debug.WriteLine($"=== Cooldown cards: {string.Join(", ", _recentlyAutoMovedCards.Select(c => c.CardType))} ===");
                
                // NUCLEAR OPTION 2: Check if THIS SPECIFIC CARD is in cooldown before marking it as draggable
                if (_recentlyAutoMovedCards.Contains(card))
                {
                    System.Diagnostics.Debug.WriteLine($"=== NUCLEAR OPTION 2: Card {card.CardType} in cooldown - blocking drag marking ===");
                    System.Diagnostics.Debug.WriteLine($"=== Cooldown check hit at: {DateTime.Now:HH:mm:ss.fff} ===");
                    continue; // Skip this card, try the next one
                }
                
                System.Diagnostics.Debug.WriteLine($"=== ULTRA DEBUG: Card {card.CardType} NOT in cooldown, proceeding with drag marking ===");
                
                // Don't prepare for drag immediately - just mark the card as potentially draggable
                // Drag preparation will happen in PointerMoved only if there's actual movement
                System.Diagnostics.Debug.WriteLine($"=== Card marked as potentially draggable ===");
                System.Diagnostics.Debug.WriteLine($"Card: {card.CardType}, Stack: {stack2.Name ?? "unnamed"}");
                System.Diagnostics.Debug.WriteLine($"Time: {DateTime.Now:HH:mm:ss.fff}");
                
                // Store minimal drag info without setting Z-index or classes
                _potentialDragCard = card;
                _potentialDragStack = stack2;
                _potentialDragStartPoint = e.GetCurrentPoint(stack2).Position;
            }

            break;
        }
    }

    private void AssociatedObjectOnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Canvas s)
        {
            var cardStacks = GetCardStacks(s);
            cardStacks.Clear();
            s.Children.Clear();
        }


        if (AssociatedObject != null)
        {
            AssociatedObject.AttachedToVisualTree -= AssociatedObjectOnAttachedToVisualTree;
            AssociatedObject.DetachedFromVisualTree -= AssociatedObjectOnDetachedFromVisualTree;
            AssociatedObject.PointerPressed -= AssociatedObjectOnPointerPressed;
            AssociatedObject.PointerMoved -= AssociatedObjectOnPointerMoved;
            AssociatedObject.PointerReleased -= AssociatedObjectOnPointerReleased;
            AssociatedObject.PointerCaptureLost -= AssociatedObjectOnPointerCaptureLost;
        }

        _containerCache.Clear();
    }

    /// <summary>
    /// Handles double-tap events on playing cards to trigger auto-move functionality.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnCardDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not PlayingCard playingCard || playingCard.DataContext is not PlayingCardViewModel card)
            return;

        // FIX: Prevent double-tap during auto-move to avoid race conditions
        var cardStacksForCheck = GetCardStacks(AssociatedObject!);
        var gameForCheck = cardStacksForCheck.FirstOrDefault()?.DataContext as CardGameViewModel;
        if (gameForCheck is KlondikeSolitaireViewModel klondikeGameForCheck && klondikeGameForCheck.IsAutoMoving)
        {
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move in progress - blocking double-tap for {card.CardType} !!!");
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move double-tap block at: {DateTime.Now:HH:mm:ss.fff} !!!");
            return; // Don't allow double-tap during auto-move
        }

        // Find the game instance
        var cardStacks = GetCardStacks(AssociatedObject!);
        var game = cardStacks.FirstOrDefault()?.DataContext as CardGameViewModel;
        
        if (game != null)
        {
            // FIX: Set cooldown IMMEDIATELY when auto-move starts to prevent re-dragging
            _recentlyAutoMovedCards.Add(card);
            _autoMoveCooldowns[card] = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: PRE-EMPTIVE cooldown set for {card.CardType} at {DateTime.Now:HH:mm:ss.fff} !!!");
            System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Pre-emptive cooldown count: {_recentlyAutoMovedCards.Count} !!!");
            
            // Try to auto-move the card (now async)
            var success = await game.TryAutoMoveCard(card);
            
            // FIX: After auto-move, ensure comprehensive cleanup
            if (success)
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move completed, performing comprehensive cleanup !!!");
                
                // 1. Clear card selection if this was the selected card
                if (game is KlondikeSolitaireViewModel klondikeGame && klondikeGame.SelectedCard == card)
                {
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Clearing selection for auto-moved card {card.CardType} !!!");
                    klondikeGame.HandleCardDeselection();
                }
                
                // 2. Remove visual selection from the card
                if (_containerCache.TryGetValue(card, out var cardControl))
                {
                    cardControl.Classes.Remove("selected");
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Removed visual selection from {card.CardType} !!!");
                }
                
                // 3. Force reset of any lingering drag state and pointer capture
                ResetDragAndKeyMove();
                
                // 4. FIX: IMMEDIATE cleanup of any newly exposed cards to prevent selection
                // Clear selection on ALL cards to ensure no newly exposed cards are selected
                if (game is KlondikeSolitaireViewModel klondikeGame2)
                {
                    klondikeGame2.HandleCardDeselection();
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: IMMEDIATE cleanup - cleared all card selection after auto-move !!!");
                }
                
                // 5. FIX: Clear visual selection from ALL cards to prevent any lingering visual state
                foreach (var kvp in _containerCache)
                {
                    kvp.Value.Classes.Remove("selected");
                }
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Cleared visual selection from ALL cards !!!");
                
                // 6. Schedule removal of cooldown after 1 second
                _ = Task.Delay(1000).ContinueWith(_ => 
                {
                    _recentlyAutoMovedCards.Remove(card);
                    _autoMoveCooldowns.Remove(card);
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Cooldown expired for {card.CardType} at {DateTime.Now:HH:mm:ss.fff} !!!");
                    System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Remaining cooldown count: {_recentlyAutoMovedCards.Count} !!!");
                });
            }
            else
            {
                // If auto-move failed, remove the pre-emptive cooldown
                _recentlyAutoMovedCards.Remove(card);
                _autoMoveCooldowns.Remove(card);
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move failed, removed pre-emptive cooldown for {card.CardType} !!!");
            }
        }
    }

    /// <summary>
    /// Handles single-tap events on playing cards to trigger selection functionality.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The event arguments.</param>
    private void OnCardSingleTapped(object? sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("=== OnCardSingleTapped called ===");
        System.Diagnostics.Debug.WriteLine($"Sender type: {sender?.GetType().Name}");
        
        if (sender is not PlayingCard playingCard)
        {
            System.Diagnostics.Debug.WriteLine("OnCardSingleTapped: Sender is not PlayingCard");
            return;
        }
        
        if (playingCard.DataContext is not PlayingCardViewModel card)
        {
            System.Diagnostics.Debug.WriteLine("OnCardSingleTapped: DataContext is not PlayingCardViewModel");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"OnCardSingleTapped: Card {card.CardType}, IsFaceDown: {card.IsFaceDown}, IsPlayable: {card.IsPlayable}");

        // Find the game instance
        var cardStacks = GetCardStacks(AssociatedObject!);
        var game = cardStacks.FirstOrDefault()?.DataContext as CardGameViewModel;
        
        if (game != null)
        {
            // FIX: Prevent any interactions during auto-move to avoid race conditions
            if (game is KlondikeSolitaireViewModel klondikeGame && klondikeGame.IsAutoMoving)
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move in progress - blocking single-tap for {card.CardType} !!!");
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Auto-move block at: {DateTime.Now:HH:mm:ss.fff} !!!");
                return; // Don't allow any interactions during auto-move
            }

            // FIX: Prevent selection of cards that are in cooldown after auto-move
            // This prevents newly exposed cards from being selected when auto-moving from waste pile
            if (_recentlyAutoMovedCards.Contains(card))
            {
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card {card.CardType} in cooldown - preventing selection !!!");
                System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Cooldown check hit in single-tap at: {DateTime.Now:HH:mm:ss.fff} !!!");
                return; // Don't allow selection of cards in cooldown
            }

            System.Diagnostics.Debug.WriteLine($"OnCardSingleTapped: Found game instance of type {game.GetType().Name}");
            
            // Check for move opportunity BEFORE changing selection
            if (game is KlondikeSolitaireViewModel klondikeGameForMove && klondikeGameForMove.SelectedCard != null)
            {
                var selectedCard = klondikeGameForMove.SelectedCard;
                
                System.Diagnostics.Debug.WriteLine($"=== Move opportunity check in OnCardSingleTapped ===");
                System.Diagnostics.Debug.WriteLine($"SelectedCard from ViewModel: {selectedCard.CardType}");
                System.Diagnostics.Debug.WriteLine($"Tapped card: {card.CardType}");
                System.Diagnostics.Debug.WriteLine($"SelectedCard == Tapped card: {ReferenceEquals(selectedCard, card)}");
                
                // Only proceed if the tapped card is different from the selected card
                if (!ReferenceEquals(selectedCard, card))
                {
                    System.Diagnostics.Debug.WriteLine($"Different card tapped - checking for move opportunity");
                    
                    // Find the target collection for the tapped card
                    var targetCollection = klondikeGameForMove.GetCardCollection(card);
                    if (targetCollection != null)
                    {
                        var sourceCollection = klondikeGameForMove.GetCardCollection(selectedCard);
                        
                        System.Diagnostics.Debug.WriteLine($"Source collection: {GetCollectionName(sourceCollection)}");
                        System.Diagnostics.Debug.WriteLine($"Target collection: {GetCollectionName(targetCollection)}");
                        System.Diagnostics.Debug.WriteLine($"Source collection reference: {sourceCollection?.GetHashCode()}");
                        System.Diagnostics.Debug.WriteLine($"Target collection reference: {targetCollection?.GetHashCode()}");
                        
                        if (sourceCollection != null && targetCollection != null && !sourceCollection.SequenceEqual(targetCollection))
                        {
                            System.Diagnostics.Debug.WriteLine($"Attempting to move selected card {selectedCard.CardType} to target collection");
                            var success = klondikeGameForMove.TryMoveSelectedCardTo(targetCollection);
                            if (success)
                            {
                                System.Diagnostics.Debug.WriteLine($"Move successful, clearing selection");
                                // Clear visual selection - the ViewModel has already cleared the selection state
                                if (_containerCache.TryGetValue(selectedCard, out var selectedCardControl))
                                {
                                    selectedCardControl.Classes.Remove("selected");
                                    System.Diagnostics.Debug.WriteLine($"Removed visual selection from {selectedCard.CardType}");
                                }
                                return; // Don't proceed with selection change
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Move failed, keeping selection for {selectedCard.CardType}");
                                // Keep the visual selection since the move failed
                                return; // Don't proceed with selection change
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Move attempt skipped - same collection or invalid source");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Same card tapped - treating as deselection");
                    // If tapping the same card, deselect it
                    klondikeGameForMove.HandleCardDeselection();
                    
                    // Remove visual selection after the ViewModel has updated the selection state
                    if (_containerCache.TryGetValue(card, out var cardControl))
                    {
                        cardControl.Classes.Remove("selected");
                        System.Diagnostics.Debug.WriteLine($"Removed visual selection from {card.CardType}");
                    }
                    
                    return; // Don't proceed with selection change
                }
            }
            
            // Handle card selection for single-tap (only if no move was attempted)
            if (game is KlondikeSolitaireViewModel klondikeGameInstance)
            {
                // FIX: Additional check to prevent selection of cards that might be newly exposed after auto-moves
                // Check if this card is in any collection that might have been affected by recent auto-moves
                var cardCollection = klondikeGameInstance.GetCardCollection(card);
                if (cardCollection != null)
                {
                    // If any card in this collection was recently auto-moved, prevent selection
                    var hasRecentlyAutoMovedCard = cardCollection.Any(c => _recentlyAutoMovedCards.Contains(c));
                    if (hasRecentlyAutoMovedCard)
                    {
                        System.Diagnostics.Debug.WriteLine($"!!! DEBUG: Card {card.CardType} in collection with recently auto-moved card - preventing selection !!!");
                        return; // Don't allow selection
                    }
                }
                
                klondikeGameInstance.HandleCardSelection(card);
                
                // Apply visual selection after the ViewModel has updated the selection state
                if (card.IsSelected)
                {
                    if (_containerCache.TryGetValue(card, out var cardControl))
                    {
                        cardControl.Classes.Add("selected");
                        System.Diagnostics.Debug.WriteLine($"Applied visual selection to {card.CardType}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not find UI control for {card.CardType} to apply visual selection");
                    }
                }
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"OnCardSingleTapped: No game instance found");
        }
        
        System.Diagnostics.Debug.WriteLine("=== OnCardSingleTapped completed ===");
    }

    private void AssociatedObjectOnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (AssociatedObject?.DataContext is not CardGameViewModel model) return;

        //    EnsureImplicitAnimations();

        var cardsList = model.Deck;
        var cardStacks = GetCardStacks(AssociatedObject);

        var homePosition = cardStacks.FirstOrDefault(i => i.IsHomeStack)?.GetCardHomePosition() ?? new Point();

        if (cardsList != null)
            foreach (var card in cardsList)
            {
                var container = new PlayingCard
                {
                    DataContext = card,
                    ZIndex = -1,
                    ClipToBounds = false
                };

                // Subscribe to the double-tap event
                container.DoubleTapped += OnCardDoubleTapped;
                // Subscribe to the single-tap event
                container.SingleTapped += OnCardSingleTapped;

                _containerCache.Add(card, container);
                AssociatedObject.Children.Add(container);

                SetCanvasPosition(container, homePosition);
            }

        foreach (var cardStack in cardStacks.Where(cardStack => cardStack.SourceItems != null))
        {
            if (cardStack.SourceItems != null)
                (cardStack.SourceItems as INotifyCollectionChanged).CollectionChanged +=
                    delegate(object? _, NotifyCollectionChangedEventArgs args)
                    {
                        SourceItemsOnCollectionChanged(cardStack, args);
                    };
        }
    }

    private void SourceItemsOnCollectionChanged(CardStackPlacementControl? control, NotifyCollectionChangedEventArgs e)
    {
        if (control is null || e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null)
        {
            return;
        }

        if (control.SourceItems == null) return;

        // Handle removal of cards to clean up event handlers
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (var oldCard in e.OldItems.Cast<PlayingCardViewModel>())
            {
                if (_containerCache.TryGetValue(oldCard, out var container))
                {
                    // Remove the double-tap event handler
                    if (container is PlayingCard playingCard)
                    {
                        playingCard.DoubleTapped -= OnCardDoubleTapped;
                        playingCard.SingleTapped -= OnCardSingleTapped;
                    }
                }
            }
            return;
        }

        foreach (var pair in control.SourceItems?.Select((card, i) => (card, i)) ?? Enumerable.Empty<(PlayingCardViewModel card, int i)>())
        {
            if (!_containerCache.TryGetValue(pair.card, out var container)) return;

            // DEBUG: Log collection order and Z-index assignment
            System.Diagnostics.Debug.WriteLine($"=== Card positioning debug ===");
            System.Diagnostics.Debug.WriteLine($"Card: {pair.card.CardType}, Index: {pair.i}, Total: {control.SourceItems?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"Z-Index assigned: {pair.i}");

            // Clear any visual selection state when cards are moved to new collections
            if (pair.card.IsSelected)
            {
                System.Diagnostics.Debug.WriteLine($"Clearing visual selection for card {pair.card.CardType} moved to new collection");
                container.Classes.Remove("selected");
            }

            var sumOffsets = control.SourceItems?
                .Select((card, i) => (card, i))
                .Where(a => a.i < pair.i)
                .Select(b =>
                {
                    GetOffsets(control, b.card, b.i, control.SourceItems?.Count ?? 0, out var c,
                        out var d);

                    return b.card.IsFaceDown ? c : d;
                })
                .Sum() ?? 0.0;

            var stackPosition = control.GetCardHomePosition();

            var pos = new Point(stackPosition.X +
                                (control.Orientation == Orientation.Horizontal ? sumOffsets : 0),
                stackPosition.Y + (control.Orientation == Orientation.Vertical ? sumOffsets : 0));

            var isLastCard = pair.i == (control.SourceItems?.Count ?? 0) - 1 || pair.i == (control.SourceItems?.Count ?? 0) - 2;
            container.Classes.Set("lastCard", isLastCard);

            // FIX: Smart Z-index calculation based on collection type
            // Tableau stacks: Face-up cards should be on top (don't invert)
            // Foundation piles: Most recent cards should be on top (don't invert - highest index should be on top)
            // Use a base Z-index to avoid conflicts with drag/keyboard operations
            var totalCards = control.SourceItems?.Count ?? 0;
            var newZIndex = 2000000000 + pair.i; // Default: don't invert
            
            // Check if this is a foundation pile (most recent cards on top)
            if (control.Name?.Contains("Foundation") == true || control.Name?.Contains("foundation") == true)
            {
                // Foundation: DON'T invert - highest index (most recent) should be on top
                // pair.i is already correct: 0=Ace (bottom), 1=2 (above), 2=3 (above), etc.
                newZIndex = 2000000000 + pair.i;
                System.Diagnostics.Debug.WriteLine($"Foundation pile detected - normal Z-index for {pair.card.CardType} (index {pair.i})");
            }
            else
            {
                // Tableau/other: keep normal order (face-up cards on top)
                newZIndex = 2000000000 + pair.i;
                System.Diagnostics.Debug.WriteLine($"Tableau/other collection - normal Z-index for {pair.card.CardType}");
            }
            
            container.ZIndex = newZIndex;
            
            // DEBUG: Track Z-index assignments with timing
            System.Diagnostics.Debug.WriteLine($"=== Z-Index Assignment Debug ===");
            System.Diagnostics.Debug.WriteLine($"Card: {pair.card.CardType}, Collection Index: {pair.i}, Z-Index: {newZIndex}");
            System.Diagnostics.Debug.WriteLine($"Time: {DateTime.Now:HH:mm:ss.fff}");
            System.Diagnostics.Debug.WriteLine($"Collection: {control.Name ?? "unnamed"}, Total Cards: {totalCards}, Type: {(control.Name?.Contains("Foundation") == true ? "Foundation" : "Tableau/Other")}");
            
            // MONITOR: Track if Z-index gets changed later (using UI thread-safe approach)
            var originalZIndex = newZIndex;
            var dispatcherTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            
            dispatcherTimer.Tick += (sender, e) =>
            {
                if (container.ZIndex != originalZIndex)
                {
                    System.Diagnostics.Debug.WriteLine($"!!! Z-Index OVERRIDE DETECTED !!!");
                    System.Diagnostics.Debug.WriteLine($"Card: {pair.card.CardType}, Original: {originalZIndex}, Current: {container.ZIndex}");
                    System.Diagnostics.Debug.WriteLine($"Time: {DateTime.Now:HH:mm:ss.fff}");
                }
                
                // Stop the timer after 1 second
                dispatcherTimer.Stop();
            };
            
            dispatcherTimer.Start();
            
            SetHomePosition(container, pos);
            
            // Check if auto-move is in progress to avoid bypassing UI transitions
            var game = control.DataContext as CardGameViewModel;
            if (game is KlondikeSolitaireViewModel klondikeGame)
            {
                // Use reflection to check the private _isAutoMoving field
                var isAutoMovingField = typeof(KlondikeSolitaireViewModel).GetField("_isAutoMoving", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var isAutoMoving = isAutoMovingField?.GetValue(klondikeGame) as bool? ?? false;
                
                if (isAutoMoving)
                {
                    // During auto-move, still update Z-index and positioning but skip immediate canvas positioning
                    // This ensures proper visual layering while allowing UI transitions to work naturally
                    System.Diagnostics.Debug.WriteLine($"Auto-move detected for {pair.card.CardType} - updating Z-index and positioning");
                    // Don't skip - let the positioning logic run, just don't force immediate canvas updates
                }
            }
            
            // Always update the canvas position to ensure proper visual layering
            SetCanvasPosition(container, pos);
            
            // Force immediate visual update to ensure proper card ordering
            if (container is Control controlContainer)
            {
                controlContainer.InvalidateVisual();
                controlContainer.InvalidateArrange();
            }
        }
    }

    private static void GetOffsets(CardStackPlacementControl parent, PlayingCardViewModel card, int n, int total,
        out double faceDownOffset,
        out double faceUpOffset)

    {
        faceDownOffset = 0;
        faceUpOffset = 0;

        //  We are now going to offset only if the offset mode is appropriate.
        switch (parent.OffsetMode)
        {
            case OffsetMode.EveryCard:
                //  Offset every card.
                faceDownOffset = parent.FaceDownOffset ?? default;
                faceUpOffset = parent.FaceUpOffset ?? default;
                break;
            case OffsetMode.EveryNthCard:
                //  Offset only if n Mod N is zero.
                if ((n + 1) % (int)parent.NValue == 0)
                {
                    faceDownOffset = parent.FaceDownOffset ?? default;
                    faceUpOffset = parent.FaceUpOffset ?? default;
                }

                break;


            case OffsetMode.TopNCards:
                //  Offset only if (Total - N) <= n < Total
                var k = (int)parent.NValue;

                if ((total - k) <= n && n < total)
                {
                    faceDownOffset = parent.FaceDownOffset ?? default;
                    faceUpOffset = parent.FaceUpOffset ?? default;
                }

                break;

            case OffsetMode.BottomNCards:
                //  Offset only if 0 < n < N
                if (n <= (int)(parent.NValue))
                {
                    faceDownOffset = parent.FaceDownOffset ?? default;
                    faceUpOffset = parent.FaceUpOffset ?? default;
                }

                break;
            case OffsetMode.UseCardValues:
                //  Offset each time by the amount specified in the card object.
                faceDownOffset = card.FaceDownOffset;
                faceUpOffset = card.FaceUpOffset;
                break;
        }
    }

    /// <summary>
    /// Updates the visual selection state of a card control.
    /// </summary>
    /// <param name="card">The card to update.</param>
    /// <param name="isSelected">Whether the card should appear selected.</param>
    public void UpdateCardVisualSelection(PlayingCardViewModel card, bool isSelected)
    {
        if (_containerCache.TryGetValue(card, out var cardControl))
        {
            if (isSelected)
            {
                cardControl.Classes.Add("selected");
                System.Diagnostics.Debug.WriteLine($"Added selection class to card {card.CardType}");
            }
            else
            {
                cardControl.Classes.Remove("selected");
                System.Diagnostics.Debug.WriteLine($"Removed selection class from card {card.CardType}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Card control not found in cache for {card.CardType}");
        }
    }

    /// <summary>
    /// Gets a readable name for a collection to help with debugging.
    /// </summary>
    private string GetCollectionName(IList<PlayingCardViewModel>? collection)
    {
        if (collection == null) return "null";
        if (collection.Count == 0) return "empty";
        
        // Try to identify the collection type based on the first few cards
        var firstCard = collection.First();
        if (firstCard.IsFaceDown)
            return "face-down cards";
        
        // Return a more specific identifier that includes the card type
        return $"cards starting with {firstCard.CardType}";
    }

    /// <summary>
    /// Gets a readable name for a stack to help with debugging.
    /// </summary>
    private string GetStackType(CardStackPlacementControl stack)
    {
        if (stack.Name != null)
            return stack.Name;
        
        if (stack.Classes.Contains("StackMarker"))
            return "StackMarker";
        
        return "unnamed stack";
    }
}