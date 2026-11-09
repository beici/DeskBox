using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Helpers;

/// <summary>
/// Tracks whether a <see cref="TextBox"/> currently has an active IME composition so
/// keyboard handlers can avoid treating the Enter that confirms a candidate as a commit.
/// </summary>
/// <remarks>
/// A composing Enter arrives as a normal <c>KeyDown</c> for <c>VirtualKey.Enter</c>; acting
/// on it commits the raw pinyin/romaji buffer as the final text. The markdown source
/// editor has guarded its own editor this way since it shipped; this helper gives the
/// rename and quick-add text boxes the same protection without duplicating the state.
/// </remarks>
internal static class TextCompositionTracker
{
    private static readonly ConditionalWeakTable<TextBox, CompositionState> States = new();

    private sealed class CompositionState
    {
        public bool IsActive;
    }

    /// <summary>
    /// Starts tracking <paramref name="textBox"/>. Safe to call more than once; the
    /// composition events are only subscribed on the first call for each instance.
    /// </summary>
    public static void Attach(TextBox? textBox)
    {
        if (textBox is null || States.TryGetValue(textBox, out _))
        {
            return;
        }

        CompositionState state = States.GetOrCreateValue(textBox);
        textBox.TextCompositionStarted += (_, _) => state.IsActive = true;
        textBox.TextCompositionEnded += (_, _) => state.IsActive = false;

        // Losing focus or being unloaded mid-composition must clear the mark too: the
        // composition is cancelled without a further TextCompositionEnded in some paths,
        // and a stale mark would swallow Enter for the rest of this text box's life.
        textBox.LostFocus += (_, _) => state.IsActive = false;
        textBox.Unloaded += (_, _) => state.IsActive = false;
    }

    /// <summary>Returns true while the text box is composing an IME candidate.</summary>
    /// <remarks>
    /// Tracking starts on first query so a handler can guard without the owner having to
    /// remember an explicit <see cref="Attach"/> call; the subscription is idempotent.
    /// </remarks>
    public static bool IsComposing(TextBox? textBox)
    {
        if (textBox is null)
        {
            return false;
        }

        Attach(textBox);
        return States.TryGetValue(textBox, out CompositionState? state) && state.IsActive;
    }
}
