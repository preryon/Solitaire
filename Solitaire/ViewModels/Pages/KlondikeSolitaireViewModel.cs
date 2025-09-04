using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Solitaire.Models;
using Solitaire.Utils;

namespace Solitaire.ViewModels.Pages;

/// <summary>
/// The Klondike Solitaire View Model.
/// </summary>
public partial class KlondikeSolitaireViewModel : CardGameViewModel
{
    /// <inheritdoc />
    public override string GameName => "Klondike";

    [ObservableProperty] private DrawMode _drawMode;
    private bool _isTurning;
 
    public KlondikeSolitaireViewModel(CasinoViewModel casinoViewModel) : base(casinoViewModel)
    {
        _casinoViewModel = casinoViewModel;
        InitializeFoundationsAndTableauSet();

        //  Create the turn stock command.
        TurnStockCommand = new AsyncRelayCommand(DoTurnStock);
        AutoMoveCommand = new AsyncRelayCommand(TryMoveAllCardsToAppropriateFoundations);
        NewGameCommand = new AsyncRelayCommand(DoDealNewGame);
    }

    private void InitializeFoundationsAndTableauSet()
    {
        //  Create the quick access arrays.
        _foundations.Add(Foundation1);
        _foundations.Add(Foundation2);
        _foundations.Add(Foundation3);
        _foundations.Add(Foundation4);
        _tableauSet.Add(Tableau1);
        _tableauSet.Add(Tableau2);
        _tableauSet.Add(Tableau3);
        _tableauSet.Add(Tableau4);
        _tableauSet.Add(Tableau5);
        _tableauSet.Add(Tableau6);
        _tableauSet.Add(Tableau7);
    }

    /// <summary>
    /// Gets the card collection for the specified card.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns></returns>Odd, it's still 
    public override IList<PlayingCardViewModel>? GetCardCollection(PlayingCardViewModel card)
    {
        System.Diagnostics.Debug.WriteLine($"GetCardCollection called for card: {card.CardType}");
        
        if (Stock.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"Card {card.CardType} found in Stock");
            return Stock;
        }
        
        if (Waste.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"Card {card.CardType} found in Waste");
            return Waste;
        }

        foreach (var foundation in _foundations.Where(foundation => foundation.Contains(card)))
        {
            System.Diagnostics.Debug.WriteLine($"Card {card.CardType} found in Foundation");
            return foundation;
        }

        System.Diagnostics.Debug.WriteLine($"Checking {_tableauSet.Count} tableaus for card {card.CardType}");
        for (int i = 0; i < _tableauSet.Count; i++)
        {
            System.Diagnostics.Debug.WriteLine($"  Checking Tableau{i + 1} (index {i}) - Contains {card.CardType}: {_tableauSet[i].Contains(card)}");
            if (_tableauSet[i].Contains(card))
            {
                System.Diagnostics.Debug.WriteLine($"Card {card.CardType} found in Tableau{i + 1} (index {i})");
                System.Diagnostics.Debug.WriteLine($"Tableau{i + 1} reference: {_tableauSet[i].GetHashCode()}");
                return _tableauSet[i];
            }
        }

        System.Diagnostics.Debug.WriteLine($"Card {card.CardType} not found in any collection!");
        return null;
    }

    private CardSuit GetSuitForFoundations(IList<PlayingCardViewModel> cell)
    {
        if (ReferenceEquals(cell, _foundations[0]))
            return CardSuit.Hearts;

        if (ReferenceEquals(cell, _foundations[1]))
            return CardSuit.Clubs;

        if (ReferenceEquals(cell, _foundations[2]))
            return CardSuit.Diamonds;

        if (ReferenceEquals(cell, _foundations[3]))
            return CardSuit.Spades;

        throw new InvalidConstraintException();
    }
    
    /// <summary>
    /// Deals a new game.
    /// </summary>
    private async Task DoDealNewGame()
    {
        DrawMode = _casinoViewModel.SettingsInstance.DrawMode;

        ResetGame();

        var playingCards = GetNewShuffledDeck();
        
        using (var stock0 = Stock.DelayNotifications())
        {
            stock0.AddRange(playingCards);
        }
        
        await Task.Delay(600);
        
        using (var stock0 = Stock.DelayNotifications())
        {
            stock0.Clear();
        }
        
        //  Now distribute them - do the tableau sets first.
        for (var i = 0; i < 7; i++)
        {
            var tempTableau = new List<PlayingCardViewModel>();

            //  We have i face down cards and 1 face up card.
            for (var j = 0; j < i; j++)
            {
                var faceDownCardViewModel = playingCards.First();
                playingCards.Remove(faceDownCardViewModel);
                faceDownCardViewModel.IsFaceDown = true;
                tempTableau.Add(faceDownCardViewModel);
            }

            //  Add the face up card.
            var faceUpCardViewModel = playingCards.First();
            playingCards.Remove(faceUpCardViewModel);
            faceUpCardViewModel.IsFaceDown = false;
            faceUpCardViewModel.IsPlayable = true;
            tempTableau.Add(faceUpCardViewModel);


            foreach (var card in tempTableau)
            {
                _tableauSet[i].Add(card);

                await Task.Delay(75);
            }
        }

        //  Finally we add every card that's left over to the stock.
        foreach (var playingCard in playingCards)
        {
            playingCard.IsFaceDown = true;
            playingCard.IsPlayable = false;
        }

        using var stockD = Stock.DelayNotifications();
        
        stockD.AddRange(playingCards);


        //  And we're done.
        StartTimer();
    }

    public override void ResetGame()
    {
        using var stockD = Stock.DelayNotifications();
        using var wasteD = Waste.DelayNotifications();

        DrawMode = _casinoViewModel.SettingsInstance.DrawMode;

        //  Call the base, which stops the timer, clears
        //  the score etc.
        ResetInternalState();

        foreach (var tableau in _tableauSet)
        {
            using var tableauD = tableau.DelayNotifications();
            tableauD.Clear();
        }

        foreach (var foundation in _foundations)
        {
            
            using var foundationD = foundation.DelayNotifications();
            foundationD.Clear();
        }

        //  Clear everything.
        stockD.Clear();
        wasteD.Clear();
    }

    /// <summary>
    /// Turns cards from the stock into the waste.
    /// </summary>
    private async Task DoTurnStock()
    {
        if(_isTurning)
            return;

        _isTurning = true;
        
        //  If the stock is empty, put every card from the waste back into the stock.
        if (Stock.Count == 0)
        {
            foreach (var card in Waste)
            {
                card.IsFaceDown = true;
                card.IsPlayable = false;
                Stock.Insert(0, card);

                await Task.Delay(175);
            }
            Waste.Clear();
        }
        else
        { 
            //  Work out how many cards to draw.
            var cardsToDraw = DrawMode switch
            {
                DrawMode.DrawOne => 1,
                DrawMode.DrawThree => 3,
                _ => 1
            };

            //  Put up to three cards in the waste.
            for (var i = 0; i < cardsToDraw; i++)
            {
                if (Stock.Count <= 0) continue;
                var card = Stock.Last();
                Stock.Remove(card);
                card.IsFaceDown = false;
                card.IsPlayable = false;
                
                // Clear any selection state when moving to waste pile
                if (card.IsSelected)
                {
                    System.Diagnostics.Debug.WriteLine($"Clearing selection for card {card.CardType} moved to waste pile");
                    card.IsSelected = false;
                }
                
                Waste.Add(card);

                await Task.Delay(175);
            }
        }

        //  Everything in the waste must be not playable,
        //  apart from the top card.
        foreach (var wasteCard in Waste)
            wasteCard.IsPlayable = wasteCard == Waste.Last();

        _isTurning = false;
    }
    /// <summary>
    /// Tries the move all cards to appropriate foundations.
    /// </summary>
    private async Task TryMoveAllCardsToAppropriateFoundations()
    {
        //  Go through the top card in each tableau - keeping
        //  track of whether we moved one.
        var keepTrying = true;
        
        while (keepTrying)
        {
            var movedACard = false;
            
            if (Waste.Count > 0)
            {
                if (TryMoveCardToAppropriateFoundation(Waste.Last()))
                {
                    movedACard = true;
                    await Task.Delay(75);
                }
            }

            foreach (var tableau in _tableauSet)
            {
                if (tableau.Count > 0)
                {
                    if (TryMoveCardToAppropriateFoundation(tableau.Last()))
                    {
                        movedACard = true;
                        await Task.Delay(75);
                    }
                }
            }

            //  We'll keep trying if we moved a card.
            keepTrying = movedACard;
        }
    }

    /// <summary>
    /// Tries the move the card to its appropriate foundation.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns>True if card moved.</returns>
    private bool TryMoveCardToAppropriateFoundation(PlayingCardViewModel card)
    {
        // Prevent auto-move from interfering with recently manually moved cards
        if (_recentlyMovedCards.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"TryMoveCardToAppropriateFoundation rejected - card recently manually moved");
            return false;
        }

        // Additional protection: check if the card was recently moved to foundation
        if (_recentlyMovedToFoundation.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"TryMoveCardToAppropriateFoundation rejected - card recently moved to foundation");
            return false;
        }

        //  Try the top of the waste first.
        if (Waste.LastOrDefault() == card)
        {
            foreach (var foundation in _foundations)
            {
                if (CheckAndMoveCard(Waste, foundation, card))
                {
                    // Mark the card as recently moved to foundation to prevent immediate re-moves
                    _recentlyMovedToFoundation.Add(card);
                    _ = Task.Delay(1500).ContinueWith(_ => _recentlyMovedToFoundation.Remove(card));
                    return true; // Return immediately after successful move
                }
            }
        }

        //  Is the card in a tableau?
        var inTableau = false;
        var i = 0;
        for (; i < _tableauSet.Count && inTableau == false; i++)
            inTableau = _tableauSet[i].Contains(card);

        //  It's if its not in a tableau and it's not the top
        //  of the waste, we cannot move it.
        if (inTableau == false)
            return false;

        //  Try and move to each foundation.
        foreach (var foundation in _foundations)
        {
            if (CheckAndMoveCard(_tableauSet[i - 1], foundation, card))
            {
                // Mark the card as recently moved to foundation to prevent immediate re-moves
                _recentlyMovedToFoundation.Add(card);
                _ = Task.Delay(1500).ContinueWith(_ => _recentlyMovedToFoundation.Remove(card));
                return true; // Return immediately after successful move
            }
        }

        //  We couldn't move the card.
        return false;
    }

    /// <summary>
    /// Tries to automatically move a card to an appropriate destination if possible.
    /// This method is called when a card is double-tapped.
    /// </summary>
    /// <param name="card">The card to try to auto-move.</param>
    /// <returns>True if the card was successfully moved, false otherwise.</returns>
    public override async Task<bool> TryAutoMoveCard(PlayingCardViewModel card)
    {
        // Add debugging to understand when this method is being called
        System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard called for {card.CardType} - IsPlayable: {card.IsPlayable}, IsFaceDown: {card.IsFaceDown}");
        
        // Only allow auto-move if the card is playable and face up
        if (!card.IsPlayable || card.IsFaceDown)
        {
            System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard rejected - not playable or face down");
            return false;
        }

        // Prevent the same card from being processed multiple times in quick succession
        if (_processingCards.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard rejected - already processing");
            return false;
        }

        // Additional protection: check if the card was recently moved
        if (_recentlyMovedCards.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard rejected - recently moved");
            return false;
        }

        try
        {
            _processingCards.Add(card);
            _isAutoMoving = true; // Signal that auto-move is in progress

            // Instead of trying to recreate the animation system, 
            // let's use the existing auto-play mechanism which already has proper timing
            System.Diagnostics.Debug.WriteLine($"Using existing auto-play system for animation");
            
            // First priority: Try to move to foundations (upper stacks)
            if (TryMoveCardToAppropriateFoundation(card))
            {
                System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard succeeded - moved to foundation");
                // Mark the card as recently moved to prevent immediate re-moves
                _recentlyMovedCards.Add(card);
                _ = Task.Delay(3000).ContinueWith(_ => _recentlyMovedCards.Remove(card)); // Increased delay to 3 seconds
                
                // Use a longer delay to allow UI transitions to complete
                await Task.Delay(400); // Increased to allow UI transitions (350ms) + buffer
                return true;
            }

            // Second priority: Try to move to other tableaus (lower stacks)
            if (TryMoveCardToAppropriateTableau(card))
            {
                System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard succeeded - moved to tableau");
                // Mark the card as recently moved to prevent immediate re-moves
                _recentlyMovedCards.Add(card);
                _ = Task.Delay(3000).ContinueWith(_ => _recentlyMovedCards.Remove(card)); // Increased delay to 3 seconds
                
                // Use a longer delay to allow UI transitions to complete
                await Task.Delay(400); // Increased to allow UI transitions (350ms) + buffer
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"TryAutoMoveCard failed - no valid moves found");
            return false;
        }
        finally
        {
            // Remove the card from processing set after a short delay
            _ = Task.Delay(100).ContinueWith(_ => _processingCards.Remove(card));
            _isAutoMoving = false; // Clear the auto-move flag
        }
    }

    /// <summary>
    /// Tries to move a card to an appropriate tableau if it can't go to a foundation.
    /// </summary>
    /// <param name="card">The card to try to move.</param>
    /// <returns>True if the card was successfully moved, false otherwise.</returns>
    private bool TryMoveCardToAppropriateTableau(PlayingCardViewModel card)
    {
        // Prevent auto-move from interfering with recently manually moved cards
        if (_recentlyMovedCards.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"TryMoveCardToAppropriateTableau rejected - card recently manually moved");
            return false;
        }

        // Check if the card is in the waste
        if (Waste.Contains(card))
        {
            return TryMoveWasteCardToTableau(card);
        }

        // Find the source tableau
        var sourceTableau = _tableauSet.FirstOrDefault(t => t.Contains(card));
        if (sourceTableau == null)
            return false;

        return TryMoveTableauCardToTableau(card, sourceTableau);
    }

    /// <summary>
    /// Tries to move a waste card to an appropriate tableau.
    /// </summary>
    /// <param name="card">The waste card to try to move.</param>
    /// <returns>True if the card was successfully moved, false otherwise.</returns>
    private bool TryMoveWasteCardToTableau(PlayingCardViewModel card)
    {
        // Try to move to tableaus where it fits
        foreach (var targetTableau in _tableauSet)
        {
            if (targetTableau.Count > 0)
            {
                // Check if the card can be placed on this tableau
                if (card.Value == 12 || // King can go on empty tableau
                    (targetTableau.Count > 0 && targetTableau.Last().Colour != card.Colour && 
                     targetTableau.Last().Value == card.Value + 1))
                {
                    if (CheckAndMoveCard(Waste, targetTableau, card))
                        return true;
                }
            }
        }

        // Try empty tableaus last (King can go anywhere)
        if (card.Value == 12)
        {
            var emptyTableau = _tableauSet.FirstOrDefault(t => t.Count == 0);
            if (emptyTableau != null)
            {
                if (CheckAndMoveCard(Waste, emptyTableau, card))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to move a tableau card to another tableau.
    /// </summary>
    /// <param name="card">The tableau card to try to move.</param>
    /// <param name="sourceTableau">The source tableau.</param>
    /// <returns>True if the card was successfully moved, false otherwise.</returns>
    private bool TryMoveTableauCardToTableau(PlayingCardViewModel card, IList<PlayingCardViewModel> sourceTableau)
    {
        // Try to move to other tableaus where it fits
        foreach (var targetTableau in _tableauSet)
        {
            if (targetTableau.Count > 0 && !sourceTableau.SequenceEqual(targetTableau))
            {
                // Check if the card can be placed on this tableau
                if (card.Value == 12 || // King can go on empty tableau
                    (targetTableau.Count > 0 && targetTableau.Last().Colour != card.Colour && 
                     targetTableau.Last().Value == card.Value + 1))
                {
                    if (CheckAndMoveCard(sourceTableau, targetTableau, card))
                        return true;
                }
            }
        }

        // Try empty tableaus last (King can go anywhere)
        if (card.Value == 12)
        {
            var emptyTableau = _tableauSet.FirstOrDefault(t => t.Count == 0);
            if (emptyTableau != null && !sourceTableau.SequenceEqual(emptyTableau))
            {
                if (CheckAndMoveCard(sourceTableau, emptyTableau, card))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves the card.
    /// </summary>
    /// <param name="from">The set we're moving from.</param>
    /// <param name="to">The set we're moving to.</param>
    /// <param name="card">The card we're moving.</param>
    /// <param name="checkOnly">if set to <c>true</c> we only check if we CAN move, but don't actually move.</param>
    /// <returns>True if a card was moved.</returns>
    public override bool CheckAndMoveCard(IList<PlayingCardViewModel> from,
        IList<PlayingCardViewModel> to,
        PlayingCardViewModel card,
        bool checkOnly = false)
    {
        // Add debugging to understand when this method is being called
        System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard called - From: {GetCollectionName(from)}, To: {GetCollectionName(to)}, Card: {card.CardType}, CheckOnly: {checkOnly}");
        
        // Add stack trace to understand where this call is coming from
        var stackTrace = new System.Diagnostics.StackTrace();
        var caller = stackTrace.GetFrame(1)?.GetMethod()?.Name ?? "Unknown";
        var callerClass = stackTrace.GetFrame(1)?.GetMethod()?.DeclaringType?.Name ?? "Unknown";
        System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard called from: {callerClass}.{caller}");
        
        // Prevent cards that were recently moved to foundations from being moved back
        if (_recentlyMovedToFoundation.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - card recently moved to foundation");
            return false;
        }
        
        //  The trivial case is where from and to are the same.
        if (from.SequenceEqual(to))
        {
            System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - same collection");
            return false;
        }

        //  This is the complicated operation.
        int scoreModifier;

        //  Are we moving from the waste?
        if (from.SequenceEqual(Waste))
        {
            //  Are we moving to a foundation?
            if (_foundations.Contains(to))
            {
                //  We can move to a foundation only if:
                //  1. It is empty and we are an ace.
                //  2. It is card SN and we are suit S and Number N+1
                 if (GetSuitForFoundations(to) == card.Suit && ((to.Count == 0 && card.Value == 0) ||
                    (to.Count > 0 && to.Last().Suit == card.Suit && to.Last().Value == card.Value - 1)))
                {
                    //  Move from waste to foundation.
                    scoreModifier = 10;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Waste to Foundation, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid waste to foundation move");
                    return false;
                }
            }
            //  Are we moving to a tableau?
            else if (_tableauSet.Contains(to))
            {
                //  We can move to a tableau only if:
                //  1. It is empty and we are a king.
                //  2. It is card CN and we are color !C and Number N-1
                if ((to.Count == 0 && card.Value == 12) ||
                    (to.Count > 0 && to.Last().Colour != card.Colour && to.Last().Value == card.Value + 1))
                {
                    //  Move from waste to tableau.
                    scoreModifier = 5;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Waste to Tableau, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid waste to tableau move");
                    return false;
                }
            }
            //  Any other move from the waste is wrong.
            else
            {
                System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid waste move");
                return false;
            }
        }
        //  Are we moving from a tableau?
        else if (_tableauSet.Contains(from))
        {
            //  Are we moving to a foundation?
            if (_foundations.Contains(to))
            {
                System.Diagnostics.Debug.WriteLine($"Tableau to Foundation move validation:");
                System.Diagnostics.Debug.WriteLine($"  Card: {card.CardType}, Value: {card.Value}, Suit: {card.Suit}");
                System.Diagnostics.Debug.WriteLine($"  Target foundation count: {to.Count}");
                System.Diagnostics.Debug.WriteLine($"  Foundation expected suit: {GetSuitForFoundations(to)}");
                System.Diagnostics.Debug.WriteLine($"  Suit match: {GetSuitForFoundations(to)} == {card.Suit} = {GetSuitForFoundations(to) == card.Suit}");
                
                // FIX: Check if the card is the top card of its stack (no cards on top)
                var cardIndex = from.IndexOf(card);
                var isTopCard = cardIndex == from.Count - 1;
                System.Diagnostics.Debug.WriteLine($"  Card index: {cardIndex}, Stack count: {from.Count}, Is top card: {isTopCard}");
                
                if (!isTopCard)
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - card is not the top card of its stack");
                    return false;
                }
                
                if (to.Count > 0)
                {
                    var targetTopCard = to.Last();
                    System.Diagnostics.Debug.WriteLine($"  Target top card: {targetTopCard.CardType}, Value: {targetTopCard.Value}, Suit: {targetTopCard.Suit}");
                    System.Diagnostics.Debug.WriteLine($"  Value check: {targetTopCard.Value} == {card.Value} - 1 = {targetTopCard.Value == card.Value - 1}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  Empty foundation - requires Ace (value 0)");
                    System.Diagnostics.Debug.WriteLine($"  Card value check: {card.Value} == 0 = {card.Value == 0}");
                }
                
                //  We can move to a foundation only if:
                //  1. It is empty and we are an ace.
                //  2. It is card SN and we are suit S and Number N+1
                //  3. The card is the top card of its stack (no cards on top)
                if (GetSuitForFoundations(to) == card.Suit && ((to.Count == 0 && card.Value == 0) ||
                    (to.Count > 0 && to.Last().Suit == card.Suit && to.Last().Value == card.Value - 1)))
                {
                    //  Move from tableau to foundation.
                    scoreModifier = 10;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Tableau to Foundation, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid tableau to foundation move");
                    if (GetSuitForFoundations(to) != card.Suit)
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Suit mismatch - foundation expects {GetSuitForFoundations(to)}, card is {card.Suit}");
                    else if (to.Count == 0 && card.Value != 0)
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Empty foundation requires Ace (value 0), but card has value {card.Value}");
                    else if (to.Count > 0 && to.Last().Suit != card.Suit)
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Top card suit {to.Last().Suit} doesn't match card suit {card.Suit}");
                    else if (to.Count > 0 && to.Last().Value != card.Value - 1)
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Top card value {to.Last().Value} should be {card.Value - 1}");
                    return false;
                }
            }
            //  Are we moving to another tableau?
            else if (_tableauSet.Contains(to))
            {
                System.Diagnostics.Debug.WriteLine($"Tableau to Tableau move validation:");
                System.Diagnostics.Debug.WriteLine($"  Card: {card.CardType}, Value: {card.Value}, Colour: {card.Colour}");
                System.Diagnostics.Debug.WriteLine($"  Target tableau count: {to.Count}");
                
                // FIX: Check if the card is the top card of its stack (no cards on top)
                var cardIndex = from.IndexOf(card);
                var isTopCard = cardIndex == from.Count - 1;
                System.Diagnostics.Debug.WriteLine($"  Card index: {cardIndex}, Stack count: {from.Count}, Is top card: {isTopCard}");
                
                if (!isTopCard)
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - card is not the top card of its stack");
                    return false;
                }
                
                if (to.Count > 0)
                {
                    var targetTopCard = to.Last();
                    System.Diagnostics.Debug.WriteLine($"  Target top card: {targetTopCard.CardType}, Value: {targetTopCard.Value}, Colour: {targetTopCard.Colour}");
                    System.Diagnostics.Debug.WriteLine($"  Colour check: {targetTopCard.Colour} != {card.Colour} = {targetTopCard.Colour != card.Colour}");
                    System.Diagnostics.Debug.WriteLine($"  Value check: {targetTopCard.Value} == {card.Value} + 1 = {targetTopCard.Value == card.Value + 1}");
                }
                
                //  We can move to a tableau only if:
                //  1. It is empty and we are a king.
                //  2. It is card CN and we are color !C and Number N-1
                //  3. The card is the top card of its stack (no cards on top)
                if ((to.Count == 0 && card.Value == 12) ||
                    (to.Count > 0 && to.Last().Colour != card.Colour && to.Last().Value == card.Value + 1))
                {
                    //  Move from tableau to tableau.
                    scoreModifier = 0;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Tableau to Tableau, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid tableau to tableau move");
                    if (to.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Empty tableau requires King (value 12), but card has value {card.Value}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  Rejection reason: Colour or value mismatch");
                    }
                    return false;
                }
            }
            //  Any other move from a tableau is wrong.
            else
            {
                System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid tableau move");
                return false;
            }
        }
        //  Are we moving from a foundation?
        else if (_foundations.Contains(from))
        {
            //  Are we moving to a tableau?
            if (_tableauSet.Contains(to))
            {
                //  We can move to a tableau only if:
                //  1. It is empty and we are a king.
                //  2. It is card CN and we are color !C and Number N-1
                if ((to.Count == 0 && card.Value == 12) ||
                    (to.Count > 0 && to.Last().Colour != card.Colour && to.Last().Value == card.Value + 1))
                {
                    //  Move from foundation to tableau.
                    scoreModifier = -15;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Foundation to Tableau, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid foundation to tableau move");
                    return false;
                }
            }
            //  Are we moving to another foundation?
            else if (_foundations.Contains(to))
            {
                if (GetSuitForFoundations(to) != card.Suit && card.Value == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid foundation to foundation move");
                    return false;
                }
                
                //  We can move from a foundation to a foundation only 
                //  if the source foundation has one card (the ace) and the
                //  destination foundation has no cards).
                if (from.Count == 1 && to.Count == 0)
                {
                    //  The move is valid, but has no impact on the score.
                    scoreModifier = 0;
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - Foundation to Foundation, Score: {scoreModifier}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid foundation to foundation move");
                    return false;
                }
            }
            //  Any other move from a foundation is wrong.
            else
            {
                System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - invalid foundation move");
                return false;
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard rejected - unknown source collection");
            return false;
        }

        //  If we were just checking, we're done.
        if (checkOnly)
        {
            System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - check only, returning true");
            return true;
        }

        //  If we've got here we've passed all tests
        //  and move the card and update the score.
        System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - executing move with score: {scoreModifier}");
        MoveCard(from, to, card, scoreModifier);
        Score += scoreModifier;
        Moves++;

        // Mark the card as recently moved to prevent auto-move from interfering
        _recentlyMovedCards.Add(card);
        _ = Task.Delay(1500).ContinueWith(_ => _recentlyMovedCards.Remove(card));

        // Clear card selection after successful move
        if (_selectedCard == card)
        {
            DeselectCard();
        }

        //  If we have moved from the waste, we must 
        //  make sure that the top of the waste is playable.
        if (from.SequenceEqual(Waste) && Waste.Count > 0)
            Waste.Last().IsPlayable = true;

        //  Check for victory.
        CheckForVictory();

        System.Diagnostics.Debug.WriteLine($"CheckAndMoveCard - move completed successfully");
        return true;
    }

    /// <summary>
    /// Actually moves the card.
    /// </summary>
    /// <param name="from">The stack we're moving from.</param>
    /// <param name="to">The stack we're moving to.</param>
    /// <param name="card">The card.</param>
    private void MoveCard(IList<PlayingCardViewModel> from,
        IList<PlayingCardViewModel> to,
        PlayingCardViewModel card, int scoreModifier)
    {
        List<PlayingCardViewModel> run;
        
        // FIX: Handle foundation moves differently - only move single cards
        if (_foundations.Contains(to))
        {
            // For foundation moves, only move the single card, not the entire stack
            var startIndex = from.IndexOf(card);
            if (startIndex >= 0)
            {
                // Remove only the single card
                from.RemoveAt(startIndex);
                to.Add(card);
                
                // Make the card playable in its new location
                card.IsPlayable = true;
                
                // If we removed a card from a tableau, make the new top card playable
                if (_tableauSet.Contains(from) && from.Count > 0)
                {
                    from.Last().IsPlayable = true;
                }
                
                // Create a single-card run for recording
                run = new List<PlayingCardViewModel> { card };
            }
            else
            {
                // Card not found, nothing to do
                return;
            }
        }
        else
        {
            // For non-foundation moves, move the entire run as before
            //  Identify the run of cards we're moving.
            run = new List<PlayingCardViewModel>();
            var startIndex = from.IndexOf(card);
            for (var i = startIndex; i < from.Count; i++)
                run.Add(from[i]);

            //  This function will move the card, as well as setting the 
            //  playable properties of the cards revealed.
            
            //  SAFETY FIX: Remove cards from the end to avoid index shifting issues
            //  Remove cards in reverse order to maintain correct indices
            for (int i = from.Count - 1; i >= startIndex; i--)
            {
                from.RemoveAt(i);
            }
            
            //  Add all cards in the run to the destination
            foreach (var runCard in run)
                to.Add(runCard);

            //  Ensure the moved card is marked as playable in its new location
            if (run.Contains(card))
            {
                card.IsPlayable = true;
            }
        }

        //  If we moved to a foundation, mark the card as recently moved to prevent immediate re-moves
        if (_foundations.Contains(to))
        {
            _recentlyMovedToFoundation.Add(card);
            _ = Task.Delay(1000).ContinueWith(_ => _recentlyMovedToFoundation.Remove(card));
        }

        //  Are there any cards left in the from pile?
        if (from.Count > 0)
        {
            //  Reveal the top card and make it playable.
            var topCardViewModel = from.Last();

            topCardViewModel.IsFaceDown = false;
            topCardViewModel.IsPlayable = true;

            RecordMoves(new MoveOperation(from, to, run, scoreModifier), new GenericOperation(() =>
            {
                topCardViewModel.IsFaceDown = true;
                topCardViewModel.IsPlayable = false;
            }));
        }
        else
        {
            RecordMoves(new MoveOperation(from, to, run, scoreModifier));
        }
    }

    /// <summary>
    /// Checks for victory.
    /// </summary>
    private void CheckForVictory()
    {
        //  We've won if every foundation is full.
        foreach (var foundation in _foundations)
            if (foundation.Count < 13)
                return;

        //  We've won.
        IsGameWon = true;

        //  Stop the timer.
        StopTimer();

        //  Fire the won event.
        FireGameWonEvent();
    }

    /// <summary>
    /// Debug method to help diagnose game state issues.
    /// </summary>
    public void DebugGameState()
    {
        System.Diagnostics.Debug.WriteLine("=== GAME STATE DEBUG ===");
        System.Diagnostics.Debug.WriteLine($"Score: {Score}, Moves: {Moves}");
        System.Diagnostics.Debug.WriteLine($"Recently moved cards: {_recentlyMovedCards.Count}");
        System.Diagnostics.Debug.WriteLine($"Recently moved to foundation: {_recentlyMovedToFoundation.Count}");
        System.Diagnostics.Debug.WriteLine($"Processing cards: {_processingCards.Count}");
        System.Diagnostics.Debug.WriteLine($"Is auto-moving: {_isAutoMoving}");
        
        // Check for face-down cards that should be face-up
        foreach (var tableau in _tableauSet)
        {
            if (tableau.Count > 0)
            {
                var topCard = tableau.Last();
                if (topCard.IsFaceDown && !topCard.IsPlayable)
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Top card of tableau is face-down and not playable: {topCard.CardType}");
                }
            }
        }
        System.Diagnostics.Debug.WriteLine("=== END DEBUG ===");
    }

    /// <summary>
    /// Handles card selection for visual feedback and ease of use.
    /// </summary>
    /// <param name="card">The card that was selected.</param>
    public void HandleCardSelection(PlayingCardViewModel card)
    {
        System.Diagnostics.Debug.WriteLine($"HandleCardSelection called for card: {card.CardType}");
        System.Diagnostics.Debug.WriteLine($"Card state - FaceDown: {card.IsFaceDown}, Playable: {card.IsPlayable}");
        
        // FIX: Prevent selection during auto-move to avoid race conditions
        if (_isAutoMoving)
        {
            System.Diagnostics.Debug.WriteLine($"HandleCardSelection blocked - auto-move in progress for {card.CardType}");
            return;
        }
        
        // FIX: Prevent selection of recently moved cards
        if (_recentlyMovedCards.Contains(card))
        {
            System.Diagnostics.Debug.WriteLine($"HandleCardSelection blocked - card {card.CardType} was recently moved");
            return;
        }
        
        // FIX: Prevent selection of cards in collections affected by recent auto-moves
        var cardCollection = GetCardCollection(card);
        if (cardCollection != null)
        {
            var hasRecentlyMovedCard = cardCollection.Any(c => _recentlyMovedCards.Contains(c));
            if (hasRecentlyMovedCard)
            {
                System.Diagnostics.Debug.WriteLine($"HandleCardSelection blocked - card {card.CardType} in collection with recently moved card");
                return;
            }
        }
        
        // Only allow selection of face-up, playable cards
        if (!card.IsFaceDown && card.IsPlayable)
        {
            System.Diagnostics.Debug.WriteLine($"Selecting card: {card.CardType}");
            SelectCard(card);
            System.Diagnostics.Debug.WriteLine($"Card selected: {card.CardType}, IsSelected: {card.IsSelected}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Card selection rejected: {card.CardType} - FaceDown: {card.IsFaceDown}, Playable: {card.IsPlayable}");
        }
    }

    /// <summary>
    /// Handles card deselection when a card is tapped again or when selection is cleared.
    /// </summary>
    public void HandleCardDeselection()
    {
        DeselectCard();
        System.Diagnostics.Debug.WriteLine("Card deselected");
    }

    /// <summary>
    /// Attempts to move the selected card to a target collection.
    /// </summary>
    /// <param name="targetCollection">The target collection to move the card to.</param>
    /// <returns>True if the move was successful, false otherwise.</returns>
    public bool TryMoveSelectedCardTo(IList<PlayingCardViewModel> targetCollection)
    {
        System.Diagnostics.Debug.WriteLine($"=== TryMoveSelectedCardTo called ===");
        System.Diagnostics.Debug.WriteLine($"Target collection: {GetCollectionName(targetCollection)}");
        
        if (_selectedCard == null)
        {
            System.Diagnostics.Debug.WriteLine("No card selected for move");
            return false;
        }

        var card = _selectedCard;
        System.Diagnostics.Debug.WriteLine($"Selected card: {card.CardType}");
        var sourceCollection = GetCardCollection(card);
        
        if (sourceCollection == null)
        {
            System.Diagnostics.Debug.WriteLine("Could not determine source collection for selected card");
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"Attempting to move {card.CardType} from {GetCollectionName(sourceCollection)} to {GetCollectionName(targetCollection)}");
        System.Diagnostics.Debug.WriteLine($"Card details - FaceDown: {card.IsFaceDown}, Playable: {card.IsPlayable}, Value: {card.Value}, Suit: {card.Suit}");
        System.Diagnostics.Debug.WriteLine($"Target collection count: {targetCollection.Count}");
        if (targetCollection.Count > 0)
        {
            var targetTopCard = targetCollection.Last();
            System.Diagnostics.Debug.WriteLine($"Target top card: {targetTopCard.CardType}, FaceDown: {targetTopCard.IsFaceDown}, Value: {targetTopCard.Value}, Suit: {targetTopCard.Suit}, Colour: {targetTopCard.Colour}");
        }

        // Try to move the card
        var success = CheckAndMoveCard(sourceCollection, targetCollection, card);
        
        if (success)
        {
            System.Diagnostics.Debug.WriteLine($"Move successful, clearing selection");
            // Clear selection after successful move
            DeselectCard();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Move failed, keeping selection for retry");
            // Keep selection if move failed so user can try again
            // Don't call DeselectCard() here
        }

        System.Diagnostics.Debug.WriteLine($"=== TryMoveSelectedCardTo completed ===");
        return success;
    }

    //  For ease of access we have arrays of the foundations and tableau set.
    private readonly List<BatchObservableCollection<PlayingCardViewModel>> _foundations = new();
    private readonly List<BatchObservableCollection<PlayingCardViewModel>> _tableauSet = new();
    private readonly CasinoViewModel _casinoViewModel;

    public BatchObservableCollection<PlayingCardViewModel> Foundation1 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Foundation2 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Foundation3 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Foundation4 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau1 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau2 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau3 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau4 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau5 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau6 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Tableau7 { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Stock { get; } = new();

    public BatchObservableCollection<PlayingCardViewModel> Waste { get; } = new();


    /// <summary>
    /// The turn stock command.
    /// </summary> 
    public ICommand? TurnStockCommand { get; }

    private readonly HashSet<PlayingCardViewModel> _processingCards = new();
    private readonly HashSet<PlayingCardViewModel> _recentlyMovedCards = new();
    private readonly HashSet<PlayingCardViewModel> _recentlyMovedToFoundation = new();
    private bool _isAutoMoving = false;
    
    /// <summary>
    /// Public property to check if auto-move is in progress.
    /// </summary>
    public bool IsAutoMoving => _isAutoMoving; // Flag to track when auto-move is in progress

    /// <summary>
    /// Helper method to get a readable name for a collection.
    /// </summary>
    private string GetCollectionName(IList<PlayingCardViewModel> collection)
    {
        if (collection == Waste) return "Waste";
        if (collection == Stock) return "Stock";
        
        for (int i = 0; i < _foundations.Count; i++)
        {
            if (collection == _foundations[i]) return $"Foundation{i + 1}";
        }
        
        for (int i = 0; i < _tableauSet.Count; i++)
        {
            if (collection == _tableauSet[i]) return $"Tableau{i + 1}";
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Updates the visual selection state of a card by finding its control and calling UpdateSelectionState.
    /// </summary>
    /// <param name="card">The card to update.</param>
    /// <param name="isSelected">Whether the card should appear selected.</param>
    protected override void UpdateCardVisualSelection(PlayingCardViewModel card, bool isSelected)
    {
        System.Diagnostics.Debug.WriteLine($"Klondike UpdateCardVisualSelection called for {card.CardType}, isSelected: {isSelected}");
        
        // Note: The visual selection state is now handled directly in the CardFieldBehavior
        // when the SingleTapped event is processed, so this method doesn't need to do anything
    }
}