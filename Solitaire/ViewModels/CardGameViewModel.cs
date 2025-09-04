using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Solitaire.Models;
using Solitaire.Utils;
using Solitaire.ViewModels.Pages;
using System.Threading.Tasks;

namespace Solitaire.ViewModels;

/// <summary>
/// Base class for a ViewModel for a card game.
/// </summary>
public abstract partial class CardGameViewModel : ViewModelBase
{
    public ImmutableArray<PlayingCardViewModel>? Deck;

    public ICommand? AutoMoveCommand { get; protected set; }

    private readonly Stack<CardOperation[]> _moveStack = new();

    public abstract string? GameName { get; }

    private void ClearUndoStack()
    {
        _moveStack.Clear();
    }
    
    
    
    protected void RecordMoves(params CardOperation[] operations)
    {
        _moveStack.Push(operations);
    }

    private void UndoMove()
    {
        // Add debugging to understand when undo is being triggered
        
        if (_moveStack.Count > 0)
        {
            try
            {
                var operations = _moveStack.Pop();

                foreach (var operation in operations)
                {
                    operation.Revert(this);
                }
            }
            catch (Exception ex)
            {
                // If undo fails, clear the stack to prevent further corruption
                _moveStack.Clear();
            }
        }
        else
        {
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CardGameViewModel"/> class.
    /// </summary>
    protected CardGameViewModel(CasinoViewModel casinoViewModel)
    {
        NavigateToCasinoCommand =
            new RelayCommand(() =>
            {
                if (Moves > 0)
                {
                    _gameStats.UpdateStatistics();
                    casinoViewModel.Save();
                }

                casinoViewModel.CurrentView = casinoViewModel.TitleInstance;
            });

        UndoCommand = new RelayCommand(UndoMove);

        DoInitialize();
    }

    private void DoInitialize()
    {
        //  Set up the timer.
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += timer_Tick;
        InitializeDeck();
    }

    protected virtual void InitializeDeck()
    {
        if (Deck is { }) return;

        var playingCards = Enum
            .GetValuesAsUnderlyingType(typeof(CardType))
            .Cast<CardType>()
            .Select(cardType => new PlayingCardViewModel(this)
                { CardType = cardType, IsFaceDown = true })
            .ToImmutableArray();

        Deck = playingCards;
    }

    protected IList<PlayingCardViewModel> GetNewShuffledDeck()
    {
        foreach (var card in Deck!)
        {
            card.Reset();
        }

        var playingCards = Deck.Value.OrderBy(_ => PlatformProviders.NextRandomDouble()).ToList();

        return playingCards.Count == 0
            ? throw new InvalidOperationException("Starting deck cannot be empty.")
            : playingCards;
    }


    public abstract IList<PlayingCardViewModel>? GetCardCollection(PlayingCardViewModel card);


    public abstract bool CheckAndMoveCard(IList<PlayingCardViewModel> from,
        IList<PlayingCardViewModel> to,
        PlayingCardViewModel card,
        bool checkOnly = false);

    /// <summary>
    /// Tries to automatically move a card to an appropriate destination if possible.
    /// This method can be overridden by specific game implementations.
    /// </summary>
    /// <param name="card">The card to try to auto-move.</param>
    /// <returns>True if the card was successfully moved, false otherwise.</returns>
    public virtual async Task<bool> TryAutoMoveCard(PlayingCardViewModel card)
    {
        // Default implementation does nothing
        // Specific game implementations should override this method
        return await Task.FromResult(false);
    }

    /// <summary>
    /// Deals a new game.
    /// </summary>
    protected void ResetInternalState()
    {
        ClearUndoStack();
        ClearCardSelection();
        
        //  Stop the timer and reset the game data.
        StopTimer();
        ElapsedTime = TimeSpan.FromSeconds(0);
        Moves = 0;
        Score = 0;
        IsGameWon = false;
        
        // Explicitly notify property changes to ensure UI updates
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(Moves));
        OnPropertyChanged(nameof(ElapsedTime));
        OnPropertyChanged(nameof(IsGameWon));
    }

    /// <summary>
    /// Starts the timer.
    /// </summary>
    protected void StartTimer()
    {
        _lastTick = DateTime.Now;
        _timer.Start();
    }

    /// <summary>
    /// Stops the timer.
    /// </summary>
    protected void StopTimer()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Handles the Tick event of the timer control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
    private void timer_Tick(object? sender, EventArgs e)
    {
        //  Get the time, update the elapsed time, record the last tick.
        var timeNow = DateTime.Now;
        ElapsedTime += timeNow - _lastTick;
        _lastTick = timeNow;
    }

    /// <summary>
    /// Fires the game won event.
    /// </summary>
    protected void FireGameWonEvent()
    {
        _gameStats.UpdateStatistics();

        var wonEvent = GameWon;
        if (wonEvent is not { })
            wonEvent?.Invoke();
    }

    /// <summary>
    /// The timer for recording the time spent in a game.
    /// </summary>
    private readonly DispatcherTimer _timer = new();

    /// <summary>
    /// The time of the last tick.
    /// </summary>
    private DateTime _lastTick;

    [ObservableProperty] private int _score;

    [ObservableProperty] private TimeSpan _elapsedTime;

    [ObservableProperty] private int _moves;

    [ObservableProperty] private bool _isGameWon;
    private GameStatisticsViewModel _gameStats = null!;

    /// <summary>
    /// Gets the go to casino command.
    /// </summary>
    /// <value>The go to casino command.</value>
    public ICommand? NavigateToCasinoCommand { get; }

    /// <summary>
    /// Gets the deal new game command.
    /// </summary>
    /// <value>The deal new game command.</value>
    public ICommand? NewGameCommand { get; protected set; }

    public ICommand? UndoCommand { get; protected set; }

    /// <summary>
    /// Occurs when the game is won.
    /// </summary>
    public event Action GameWon = null!;

    public abstract void ResetGame();

    public void RegisterStatsInstance(GameStatisticsViewModel gameStatsInstance)
    {
        _gameStats = gameStatsInstance;
    }

    public abstract class CardOperation
    {
        public abstract void Revert(CardGameViewModel game);
    }

    /// <summary>
    /// Currently selected card for visual feedback and ease of use.
    /// </summary>
    protected PlayingCardViewModel? _selectedCard;

    /// <summary>
    /// Gets the currently selected card.
    /// </summary>
    public PlayingCardViewModel? SelectedCard => _selectedCard;

    /// <summary>
    /// Selects a card for visual feedback and ease of use.
    /// </summary>
    /// <param name="card">The card to select.</param>
    public virtual void SelectCard(PlayingCardViewModel card)
    {
        
        // FIX: Prevent any selection during auto-move to avoid race conditions
        if (this is KlondikeSolitaireViewModel klondikeGame && klondikeGame.IsAutoMoving)
        {
            return;
        }
        
        // Deselect previously selected card
        if (_selectedCard != null)
        {
            _selectedCard.IsSelected = false;
            
            // Update visual state of the previously selected card
            UpdateCardVisualSelection(_selectedCard, false);
        }

        // Select the new card
        _selectedCard = card;
        card.IsSelected = true;
        
        
        // Update visual state of the newly selected card
        UpdateCardVisualSelection(card, true);
    }

    /// <summary>
    /// Deselects the currently selected card.
    /// </summary>
    public virtual void DeselectCard()
    {
        if (_selectedCard != null)
        {
            _selectedCard.IsSelected = false;
            
            // Update visual state of the deselected card
            UpdateCardVisualSelection(_selectedCard, false);
            
            _selectedCard = null;
        }
    }

    /// <summary>
    /// Clears the card selection when resetting the game.
    /// </summary>
    protected void ClearCardSelection()
    {
        DeselectCard();
    }

    /// <summary>
    /// Updates the visual selection state of a card by finding its control and calling UpdateSelectionState.
    /// </summary>
    /// <param name="card">The card to update.</param>
    /// <param name="isSelected">Whether the card should appear selected.</param>
    protected virtual void UpdateCardVisualSelection(PlayingCardViewModel card, bool isSelected)
    {
        // This method will be overridden by derived classes to provide access to the card controls
    }

    public class GenericOperation : CardOperation
    {
        private readonly Action _action;
        
        public GenericOperation(Action action)
        {
            _action = action;
        }

        public override void Revert(CardGameViewModel game)
        {
            _action();
        }
    }

    public class MoveOperation : CardOperation
    {
        public MoveOperation(IList<PlayingCardViewModel> from, IList<PlayingCardViewModel> to, IList<PlayingCardViewModel> run,
            int score)
        {
            From = from;
            To = to;
            Run = run;
            Score = score;
        }

        public IList<PlayingCardViewModel> From { get; }

        public IList<PlayingCardViewModel> To { get; }

        public IList<PlayingCardViewModel> Run { get; }

        public int Score { get; }
        
        public override void Revert(CardGameViewModel game)
        {
            // Revert the score and moves
            game.Score -= Score;
            game.Moves--;

            // Create a copy of the run to avoid modification during iteration
            var runCopy = Run.ToList();
            
            // Remove cards from destination first
            foreach (var runCard in runCopy)
            {
                To.Remove(runCard);
            }
            
            // Then add cards back to source
            foreach (var runCard in runCopy)
            {
                From.Add(runCard);
            }
        }
    }
}